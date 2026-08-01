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

   /// <summary>The open generic <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c> a relation definition class derives from.</summary>
   public const string RELATION_DEFINITION_FULL_NAME = "mvdmio.Database.PgSQL.Relations.RelationDefinition`2";

   /// <summary>The <c>[Column]</c> named argument that declares a tenancy column.</summary>
   private const string TENANCY_PROPERTY_NAME = "Tenancy";

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

   /// <summary>
   ///    Whether a property that is not a mappable column still has to be validated, because an attribute on it says the
   ///    developer meant it to be one.
   /// </summary>
   public static bool ShouldValidateProperty(IPropertySymbol property)
   {
      return property.DeclaredAccessibility == Accessibility.Public || HasRelevantAttribute(property);
   }

   /// <summary>
   ///    Whether a property is a relation rather than a column candidate. Entirely type-driven: a property whose
   ///    type, or whose collection element type for a relation to many, derives from
   ///    <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c> is a relation on its own. Writing <c>[Relation]</c> on
   ///    it besides is accepted and changes nothing; writing it on a property this method answers
   ///    <see langword="false" /> for is what <c>PGSQL0033</c> catches.
   /// </summary>
   public static bool IsRelationProperty(IPropertySymbol property, Compilation compilation)
   {
      var type = property.Type;

      if (type is INamedTypeSymbol { IsGenericType: true } collection && _toManyCollectionTypeNames.Contains(collection.OriginalDefinition.ToDisplayString()))
         type = collection.TypeArguments[0];

      return TryGetRelationDefinitionBase(type, compilation, out _);
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

   /// <remarks>
   ///    Unlike <see cref="IsSupportedProperty" />, neither the property's own accessibility nor its accessors' is
   ///    looked at here. A relation property is purely declarative in the same sense the table definition holding it
   ///    is: nothing ever reads or writes it at run time, only its type identifies the relation. That is what lets it
   ///    be typed as a privately nested relation definition class — C# itself then requires the property to be no
   ///    more accessible than that type, exactly as it would for any other member, and this check does not relax that.
   /// </remarks>
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
         isNullable: TypeCanHoldNull(property.Type),
         isDeclaredNotNull: nullability.IsNotNull,
         nullabilityContradiction: nullability.Contradiction,
         requiresNullForgivingInitializer: property.Type.IsReferenceType && property.NullableAnnotation != NullableAnnotation.Annotated,
         storage: ColumnStorage.Read(property.Type, columnAttribute)
      );
   }

   /// <summary>Whether a <c>[Column]</c> argument, named rather than positional, was set to <see langword="true" />.</summary>
   private static bool HasNamedFlagSet(AttributeData? attribute, string propertyName)
   {
      if (attribute is null)
         return false;

      return attribute.NamedArguments.Any(x => string.Equals(x.Key, propertyName, StringComparison.Ordinal) && x.Value.Value is true);
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
   /// <summary>What a relation property's type states, or <see langword="null" /> when it states nothing usable.</summary>
   /// <remarks>
   ///    A property whose type — or whose collection element type, for a relation to many — derives from
   ///    <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c> is a relation: the target and the declaring type
   ///    argument are read from that base type, alongside the definition class itself so the caller can go on to read
   ///    its <c>Keys</c>. Called only for a property <see cref="IsRelationProperty" /> already answered
   ///    <see langword="true" /> for, so <see langword="null" /> here means the declaration is malformed rather than
   ///    that the property is not a relation at all — <c>TTarget</c> resolving to something other than a named type,
   ///    for instance.
   /// </remarks>
   public static RelationPropertyShape? ReadRelationPropertyShape(ITypeSymbol propertyType, Compilation compilation)
   {
      if (propertyType is INamedTypeSymbol { IsGenericType: true } collection
          && _toManyCollectionTypeNames.Contains(collection.OriginalDefinition.ToDisplayString()))
      {
         return ReadTargetCandidate(collection.TypeArguments[0], compilation, isToMany: true);
      }

      return ReadTargetCandidate(propertyType, compilation, isToMany: false);
   }

   /// <summary>
   ///    Whether <paramref name="type" /> derives from <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c>, and if
   ///    so, that closed base type — from which the two type arguments are read.
   /// </summary>
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

   /// <summary>
   ///    Whether <paramref name="candidate" /> derives from <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c>, and
   ///    if so, that base type's two type arguments — the declaring type and the target. Fails when
   ///    <c>TTarget</c> resolves to something other than a named type, which is the one way a relation definition
   ///    class can compile and still not name a usable target.
   /// </summary>
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
