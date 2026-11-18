using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class TableDefinitionSymbols
{
   public const string TABLE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.TableAttribute";
   public const string PRIMARY_KEY_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.PrimaryKeyAttribute";
   public const string UNIQUE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.UniqueAttribute";
   public const string COLUMN_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.ColumnAttribute";
   public const string GENERATED_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.GeneratedAttribute";
   public const string RELATION_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.RelationAttribute";

   public const string RELATION_DEFINITION_FULL_NAME = "mvdmio.Database.PgSQL.Relations.RelationDefinition`2";

   private const string TENANCY_PROPERTY_NAME = "Tenancy";

   private static readonly HashSet<string> _toManyCollectionTypeNames = new(StringComparer.Ordinal) {
      "System.Collections.Generic.List<T>",
      "System.Collections.Generic.IList<T>",
      "System.Collections.Generic.ICollection<T>",
      "System.Collections.Generic.IEnumerable<T>",
      "System.Collections.Generic.IReadOnlyList<T>",
      "System.Collections.Generic.IReadOnlyCollection<T>"
   };

   private static readonly SymbolDisplayFormat _typeDisplayFormat = new(
      globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
      miscellaneousOptions:
      SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
      SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
      SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
   );

   public static bool HasAttribute(IPropertySymbol property, string fullName)
   {
      return property.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == fullName);
   }

   public static bool ShouldValidateProperty(IPropertySymbol property)
   {
      return property.DeclaredAccessibility == Accessibility.Public || HasRelevantAttribute(property);
   }

   public static bool IsRelationProperty(IPropertySymbol property, Compilation compilation)
   {
      var (elementType, _) = ReadCardinality(property.Type);

      return TryGetRelationDefinitionBase(elementType, compilation, out _);
   }

   public static bool IsSupportedProperty(IPropertySymbol property)
   {
      return !property.IsStatic
             && property.DeclaredAccessibility == Accessibility.Public
             && property.Parameters.Length == 0
             && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
             && property.SetMethod is not null;
   }

   public static bool IsSupportedRelationPropertyShape(IPropertySymbol property)
   {
      return !property.IsStatic
             && property.Parameters.Length == 0
             && property.GetMethod is not null
             && property.SetMethod is not null;
   }

   public static bool IsPartial(INamedTypeSymbol classSymbol)
   {
      return classSymbol.DeclaringSyntaxReferences
         .Select(x => x.GetSyntax())
         .OfType<ClassDeclarationSyntax>()
         .Any(x => x.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
   }

   public static PropertyDefinitionModel CreatePropertyModel(IPropertySymbol property)
   {
      var columnAttribute = property.GetAttributes()
         .FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == COLUMN_ATTRIBUTE_FULL_NAME);

      var columnName = columnAttribute?.ConstructorArguments.FirstOrDefault().Value as string;
      var isPrimaryKey = HasAttribute(property, PRIMARY_KEY_ATTRIBUTE_FULL_NAME);
      var nullability = NullabilityClaim.Read(property, columnAttribute, isPrimaryKey);

      return new PropertyDefinitionModel(
         propertyName: property.Name,
         parameterName: ToCamelCase(property.Name),
         typeName: property.Type.ToDisplayString(_typeDisplayFormat),
         columnName: string.IsNullOrWhiteSpace(columnName) ? ToSnakeCase(property.Name) : columnName!,
         isPrimaryKey: isPrimaryKey,
         isUnique: HasAttribute(property, UNIQUE_ATTRIBUTE_FULL_NAME),
         isGenerated: HasAttribute(property, GENERATED_ATTRIBUTE_FULL_NAME),
         isTenancy: HasNamedFlagSet(columnAttribute, TENANCY_PROPERTY_NAME),
         typeCanHoldNull: TypeCanHoldNull(property.Type),
         nullability: nullability,
         requiresNullForgivingInitializer: property.Type.IsReferenceType && property.NullableAnnotation != NullableAnnotation.Annotated,
         storage: ColumnStorage.Read(property.Type, columnAttribute)
      );
   }

   private static bool HasNamedFlagSet(AttributeData? attribute, string propertyName)
   {
      if (attribute is null)
         return false;

      return attribute.NamedArguments.Any(x => string.Equals(x.Key, propertyName, StringComparison.Ordinal) && x.Value.Value is true);
   }

   public static string TypeDisplayName(ITypeSymbol type)
   {
      return type.ToDisplayString(_typeDisplayFormat);
   }

   public static bool TypeCanHoldNull(ITypeSymbol type)
   {
      return type.NullableAnnotation == NullableAnnotation.Annotated
             || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
   }

   public static bool TypeStatesNotNull(IPropertySymbol property)
   {
      if (property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
         return false;

      if (property.Type.IsValueType)
         return true;

      return property.NullableAnnotation == NullableAnnotation.NotAnnotated;
   }

   public static RelationPropertyShape? ReadRelationPropertyShape(ITypeSymbol propertyType, Compilation compilation)
   {
      var (elementType, isToMany) = ReadCardinality(propertyType);

      return ReadTargetCandidate(elementType, compilation, isToMany);
   }

   private static (ITypeSymbol ElementType, bool IsToMany) ReadCardinality(ITypeSymbol propertyType)
   {
      if (propertyType is INamedTypeSymbol { IsGenericType: true } collection
          && _toManyCollectionTypeNames.Contains(collection.OriginalDefinition.ToDisplayString()))
      {
         return (collection.TypeArguments[0], true);
      }

      return (propertyType, false);
   }

   public static bool TryGetRelationDefinitionBase(ITypeSymbol type, Compilation compilation, out INamedTypeSymbol relationDefinitionBase)
   {
      relationDefinitionBase = null!;

      var relationDefinitionSymbol = compilation.GetTypeByMetadataName(RELATION_DEFINITION_FULL_NAME);
      if (relationDefinitionSymbol is null)
         return false;

      for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
      {
         if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, relationDefinitionSymbol))
         {
            relationDefinitionBase = current;
            return true;
         }
      }

      return false;
   }

   public static Location PropertyLocation(
      ImmutableArray<IPropertySymbol> mappedProperties,
      PropertyDefinitionModel property,
      ClassDeclarationSyntax classSyntax
   )
   {
      var symbol = mappedProperties.FirstOrDefault(x => string.Equals(x.Name, property.PropertyName, StringComparison.Ordinal));

      return symbol?.Locations.FirstOrDefault() ?? classSyntax.Identifier.GetLocation();
   }

   public static string GetFullName(INamedTypeSymbol type)
   {
      return type.ContainingNamespace.IsGlobalNamespace
         ? type.Name
         : $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}";
   }

   public static bool HasGeneratedTypeNameCollision(INamedTypeSymbol classSymbol, string typeName)
   {
      return classSymbol.ContainingNamespace
         .GetTypeMembers(typeName)
         .Any(type => !CanMergeWithGeneratedType(type));
   }

   public static string GetEntityName(string className)
   {
      return className.EndsWith("Table", StringComparison.Ordinal) && className.Length > "Table".Length
         ? className.Substring(0, className.Length - "Table".Length)
         : className;
   }

   public static bool TryParseTableName(string value, out string schemaName, out string tableName)
   {
      var parts = value.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
         .Select(x => x.Trim())
         .ToArray();
      if (parts.Length == 1)
      {
         schemaName = "public";
         tableName = parts[0];
         return !string.IsNullOrWhiteSpace(tableName);
      }

      if (parts.Length == 2)
      {
         schemaName = parts[0];
         tableName = parts[1];
         return !string.IsNullOrWhiteSpace(schemaName) && !string.IsNullOrWhiteSpace(tableName);
      }

      schemaName = string.Empty;
      tableName = string.Empty;
      return false;
   }

   private static RelationPropertyShape? ReadTargetCandidate(ITypeSymbol candidate, Compilation compilation, bool isToMany)
   {
      if (candidate is not INamedTypeSymbol { TypeKind: TypeKind.Class } named)
         return null;

      if (!TryGetRelationDefinitionBase(named, compilation, out var relationDefinitionBase))
         return null;

      if (relationDefinitionBase.TypeArguments[1] is not INamedTypeSymbol target)
         return null;

      return new RelationPropertyShape(
         target,
         isToMany,
         relationDefinition: named,
         declaringTypeArgument: relationDefinitionBase.TypeArguments[0] as INamedTypeSymbol
      );
   }

   private static bool HasRelevantAttribute(IPropertySymbol property)
   {
      return HasAttribute(property, PRIMARY_KEY_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, UNIQUE_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, COLUMN_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, GENERATED_ATTRIBUTE_FULL_NAME);
   }

   private static bool CanMergeWithGeneratedType(INamedTypeSymbol type)
   {
      if (type.TypeKind != TypeKind.Class)
         return false;

      return type.DeclaringSyntaxReferences
         .Select(x => x.GetSyntax())
         .OfType<ClassDeclarationSyntax>()
         .All(x => x.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
   }

   private static string ToCamelCase(string value)
   {
      if (string.IsNullOrEmpty(value) || char.IsLower(value[0]))
         return value;

      return char.ToLowerInvariant(value[0]) + value.Substring(1);
   }

   private static string ToSnakeCase(string value)
   {
      if (string.IsNullOrEmpty(value))
         return value;

      var builder = new StringBuilder(value.Length + 5);
      for (var i = 0; i < value.Length; i++)
      {
         var current = value[i];
         if (char.IsUpper(current))
         {
            if (i > 0)
               builder.Append('_');

            builder.Append(char.ToLowerInvariant(current));
            continue;
         }

         builder.Append(current);
      }

      return builder.ToString();
   }
}
