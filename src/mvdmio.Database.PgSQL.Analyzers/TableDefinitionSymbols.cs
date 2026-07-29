using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Everything a table definition's Roslyn symbols say: which attributes a property carries, whether its shape and
///    nullability are usable, what a relation property's type states, and the names derived from all of it.
/// </summary>
/// <remarks>
///    Separated from <see cref="TableDefinitionParser" /> so that file is left with the decisions — which diagnostic a
///    fact earns and whether it abandons the table — rather than the symbol reading those decisions rest on.
/// </remarks>
internal static class TableDefinitionSymbols
{
   public const string TABLE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.TableAttribute";
   public const string PRIMARY_KEY_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.PrimaryKeyAttribute";
   public const string UNIQUE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.UniqueAttribute";
   public const string COLUMN_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.ColumnAttribute";
   public const string GENERATED_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.GeneratedAttribute";
   public const string RELATION_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.RelationAttribute";

   /// <summary>
   ///    The collection types a relation to many rows may be declared as. The generated mirror is always a concrete
   ///    list, so this only decides what the table definition itself is allowed to say.
   /// </summary>
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

   public static AttributeData RelationAttributeOf(IPropertySymbol property)
   {
      return property.GetAttributes().First(x => x.AttributeClass?.ToDisplayString() == RELATION_ATTRIBUTE_FULL_NAME);
   }

   /// <summary>
   ///    Whether a property that is not a mappable column still has to be validated, because an attribute on it says the
   ///    developer meant it to be one.
   /// </summary>
   public static bool ShouldValidateProperty(IPropertySymbol property)
   {
      return property.DeclaredAccessibility == Accessibility.Public || HasRelevantAttribute(property);
   }

   /// <remarks>
   ///    A setter has to exist, and its accessibility is not looked at. The requirement that one exist is what keeps a
   ///    computed member out — a get-only or expression-bodied property describes no column, and admitting it would turn
   ///    an expression into a column that is not there. How accessible it is says nothing about the column, because a
   ///    table definition is purely declarative and is never instantiated: <c>{ get; private set; }</c>,
   ///    <c>{ get; init; }</c> and <c>{ get; protected set; }</c> all describe the same column as <c>{ get; set; }</c>.
   /// </remarks>
   public static bool IsSupportedProperty(IPropertySymbol property)
   {
      return !property.IsStatic
             && property.DeclaredAccessibility == Accessibility.Public
             && property.Parameters.Length == 0
             && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
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
         isNullable: TypeCanHoldNull(property.Type),
         isDeclaredNotNull: nullability.IsNotNull,
         nullabilityContradiction: nullability.Contradiction,
         requiresNullForgivingInitializer: property.Type.IsReferenceType && property.NullableAnnotation != NullableAnnotation.Annotated,
         storage: ColumnStorage.Read(property.Type, columnAttribute)
      );
   }

   /// <summary>How generated code names a type: fully qualified, keywords for the special types, nullability included.</summary>
   public static string TypeDisplayName(ITypeSymbol type)
   {
      return type.ToDisplayString(_typeDisplayFormat);
   }

   /// <summary>
   ///    Whether the property's type can hold null, which a primary key member's may not.
   /// </summary>
   /// <remarks>
   ///    Both forms are checked because they are separate facts: a nullable value type is a constructed
   ///    <see cref="Nullable{T}" />, while a nullable reference type is only an annotation — and in a nullable-oblivious
   ///    file that annotation is absent, which is read here as not nullable because nothing else can be read from it.
   /// </remarks>
   public static bool TypeCanHoldNull(ITypeSymbol type)
   {
      return type.NullableAnnotation == NullableAnnotation.Annotated
             || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
   }

   /// <summary>
   ///    Whether the property's type states that its column cannot hold null. Not the negation of
   ///    <see cref="TypeCanHoldNull" />: this is the stricter question of whether the type says anything at all, and an
   ///    unannotated reference type in a nullable-oblivious file says nothing, so both answer false for it.
   /// </summary>
   /// <remarks>
   ///    A value type states it unless it is a <see cref="Nullable{T}" />. A reference type states it only through its
   ///    annotation, which is why the absence of one is read as saying nothing rather than as saying not-null.
   /// </remarks>
   public static bool TypeStatesNotNull(IPropertySymbol property)
   {
      if (property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
         return false;

      if (property.Type.IsValueType)
         return true;

      return property.NullableAnnotation == NullableAnnotation.NotAnnotated;
   }

   /// <summary>
   ///    Reads the relation's target and its cardinality off the property's type, which is the only place either is
   ///    stated.
   /// </summary>
   public static bool TryGetRelationTarget(ITypeSymbol propertyType, out INamedTypeSymbol target, out bool isToMany)
   {
      target = null!;
      isToMany = false;

      if (propertyType is INamedTypeSymbol { IsGenericType: true } collection
          && _toManyCollectionTypeNames.Contains(collection.OriginalDefinition.ToDisplayString()))
      {
         isToMany = true;

         return IsTargetCandidate(collection.TypeArguments[0], out target);
      }

      // A sequence this does not support is rejected as an unsupported type rather than read as a single target, so
      // the diagnostic names the real mistake instead of complaining that the collection is not a table definition.
      if (propertyType.SpecialType != SpecialType.System_String && IsSequence(propertyType))
         return false;

      return IsTargetCandidate(propertyType, out target);
   }

   /// <summary>
   ///    Reads the foreign-key property names off the relation attribute, in declaration order. The parameter is
   ///    variadic, so a single name and several arrive the same way.
   /// </summary>
   public static ImmutableArray<string> GetForeignKeyPropertyNames(AttributeData relationAttribute)
   {
      var argument = relationAttribute.ConstructorArguments.FirstOrDefault();

      if (argument.Kind != TypedConstantKind.Array || argument.IsNull)
         return ImmutableArray<string>.Empty;

      return argument.Values
         .Select(x => x.Value as string ?? string.Empty)
         .ToImmutableArray();
   }

   /// <summary>
   ///    Where a mapped property was declared, so a diagnostic about it points at the property rather than at the class.
   /// </summary>
   public static Location PropertyLocation(
      ImmutableArray<IPropertySymbol> mappedProperties,
      PropertyDefinitionModel property,
      ClassDeclarationSyntax classSyntax
   )
   {
      var symbol = mappedProperties.FirstOrDefault(x => string.Equals(x.Name, property.PropertyName, StringComparison.Ordinal));

      return symbol?.Locations.FirstOrDefault() ?? classSyntax.Identifier.GetLocation();
   }

   /// <summary>The namespace-qualified name of a type, which is how a relation names its target.</summary>
   public static string GetFullName(INamedTypeSymbol type)
   {
      return type.ContainingNamespace.IsGlobalNamespace
         ? type.Name
         : $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}";
   }

   /// <summary>Whether a generated type name is already taken by something it cannot merge with.</summary>
   public static bool HasGeneratedTypeNameCollision(INamedTypeSymbol classSymbol, string typeName)
   {
      return classSymbol.ContainingNamespace
         .GetTypeMembers(typeName)
         .Any(type => !CanMergeWithGeneratedType(type));
   }

   /// <summary>A table definition's class name with the <c>Table</c> suffix removed.</summary>
   public static string GetEntityName(string className)
   {
      return className.EndsWith("Table", StringComparison.Ordinal) && className.Length > "Table".Length
         ? className.Substring(0, className.Length - "Table".Length)
         : className;
   }

   /// <summary>Splits a <c>[Table]</c> value into its schema and table, defaulting the schema to <c>public</c>.</summary>
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

   private static bool IsTargetCandidate(ITypeSymbol candidate, out INamedTypeSymbol target)
   {
      if (candidate is INamedTypeSymbol { TypeKind: TypeKind.Class } named)
      {
         target = named;
         return true;
      }

      target = null!;
      return false;
   }

   private static bool IsSequence(ITypeSymbol type)
   {
      return type is IArrayTypeSymbol
             || type.AllInterfaces.Any(x => x.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
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
