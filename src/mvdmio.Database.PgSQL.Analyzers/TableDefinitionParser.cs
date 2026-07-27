using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class TableDefinitionParser
{
   private const string TABLE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.TableAttribute";
   private const string PRIMARY_KEY_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.PrimaryKeyAttribute";
   private const string UNIQUE_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.UniqueAttribute";
   private const string COLUMN_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.ColumnAttribute";
   private const string GENERATED_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.GeneratedAttribute";
   private const string RELATION_ATTRIBUTE_FULL_NAME = "mvdmio.Database.PgSQL.Attributes.RelationAttribute";

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

   internal sealed class ParseResult
   {
      public ParseResult(TableDefinitionModel? model, ImmutableArray<Diagnostic> diagnostics)
      {
         Model = model;
         Diagnostics = diagnostics;
      }

      public TableDefinitionModel? Model { get; }
      public ImmutableArray<Diagnostic> Diagnostics { get; }
   }

   public static ParseResult Parse(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
   {
      cancellationToken.ThrowIfCancellationRequested();

      var classSymbol = (INamedTypeSymbol)context.TargetSymbol;
      var classSyntax = (ClassDeclarationSyntax)context.TargetNode;
      var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

      var tableAttribute = classSymbol.GetAttributes().FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == TABLE_ATTRIBUTE_FULL_NAME);
      var tableName = tableAttribute?.ConstructorArguments.FirstOrDefault().Value as string;

      if (string.IsNullOrWhiteSpace(tableName) || !TryParseTableName(tableName!, out var schemaName, out var unqualifiedTableName))
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.InvalidTableName,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            tableName ?? string.Empty
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      if (!IsPartial(classSymbol))
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.TableClassMustBePartial,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var allProperties = classSymbol.GetMembers()
         .OfType<IPropertySymbol>()
         .ToImmutableArray();

      // The relation attribute is the only opt-out from column mapping, and it opts out its own property only.
      var relationProperties = allProperties.Where(x => HasAttribute(x, RELATION_ATTRIBUTE_FULL_NAME)).ToImmutableArray();
      var columnCandidates = allProperties.Where(x => !HasAttribute(x, RELATION_ATTRIBUTE_FULL_NAME)).ToImmutableArray();

      var invalidProperties = columnCandidates
         .Where(ShouldValidateProperty)
         .Where(x => !IsSupportedProperty(x))
         .ToImmutableArray();

      if (invalidProperties.Length > 0)
      {
         foreach (var property in invalidProperties)
         {
            diagnostics.Add(Diagnostic.Create(
               TableRepositoryDiagnostics.UnsupportedPropertyShape,
               property.Locations.FirstOrDefault() ?? classSyntax.Identifier.GetLocation(),
               classSymbol.Name,
               property.Name
            ));
         }

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var mappedProperties = columnCandidates.Where(IsSupportedProperty).ToImmutableArray();

      foreach (var property in mappedProperties.Where(x => !QueryMappableTypes.IsMappable(x.Type)))
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.UnmappableQueryPropertyType,
            property.Locations.FirstOrDefault() ?? classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            property.Name,
            property.Type.ToDisplayString()
         ));
      }

      var properties = mappedProperties
         .Select(CreatePropertyModel)
         .ToImmutableArray();

      var primaryKeys = properties.Where(x => x.IsPrimaryKey).ToImmutableArray();
      if (primaryKeys.Length != 1)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.TableClassMustHaveSinglePrimaryKey,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            primaryKeys.Length
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var duplicateColumn = properties
         .GroupBy(x => x.ColumnName, StringComparer.OrdinalIgnoreCase)
         .FirstOrDefault(x => x.Count() > 1);

      if (duplicateColumn is not null)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.DuplicateMappedColumnName,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            duplicateColumn.Key
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var primaryKey = primaryKeys[0];
      var lookupProperties = properties.Where(x => x.IsPrimaryKey || x.IsUnique).ToImmutableArray();
      var duplicateLookup = lookupProperties
         .Select(x => $"GetBy{x.PropertyName}Async")
         .GroupBy(x => x, StringComparer.Ordinal)
         .FirstOrDefault(x => x.Count() > 1);

      if (duplicateLookup is not null)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.DuplicateLookupMethodName,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            duplicateLookup.Key.Replace("GetBy", string.Empty).Replace("Async", string.Empty)
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var mutableUpdateProperties = properties.Where(x => !x.IsPrimaryKey && !x.IsGenerated).ToImmutableArray();
      if (mutableUpdateProperties.Length == 0)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.NoUpdatableColumns,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var entityName = GetEntityName(classSymbol.Name);
      var generatedTypeNames = new[]
      {
         $"{entityName}Data",
         $"Create{entityName}Command",
         $"Update{entityName}Command",
         $"I{entityName}Repository",
         $"{entityName}Repository"
      };

      var generatedNameCollision = generatedTypeNames.FirstOrDefault(typeName => HasGeneratedTypeNameCollision(classSymbol, typeName));
      if (generatedNameCollision is not null)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.GeneratedTypeNameCollision,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            generatedNameCollision
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var relations = ParseRelations(classSymbol, relationProperties, diagnostics);
      var accessibility = classSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
      var model = new TableDefinitionModel(
         classSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : classSymbol.ContainingNamespace.ToDisplayString(),
         accessibility,
         classSymbol.Name,
         GetFullName(classSymbol),
         entityName,
         $"{entityName}Data",
         $"Create{entityName}Command",
         $"Update{entityName}Command",
         $"I{entityName}Repository",
         $"{entityName}Repository",
         schemaName,
         unqualifiedTableName,
         primaryKey,
         properties,
         properties.Where(x => !x.IsGenerated).ToImmutableArray(),
         ImmutableArray.Create(primaryKey).AddRange(mutableUpdateProperties),
         lookupProperties,
         mutableUpdateProperties,
         relations
      );

      return new ParseResult(model, diagnostics.ToImmutable());
   }

   /// <remarks>
   ///    Only what one table can decide on its own is checked here: the property's shape, its type, and whether it
   ///    contradicts a column attribute. Whether the target is a table definition and whether the named foreign key
   ///    exists and can join are cross-table questions, answered once every table has been parsed. A relation that
   ///    fails a check here is dropped and the rest of the table still generates.
   /// </remarks>
   private static ImmutableArray<RelationDeclarationModel> ParseRelations(
      INamedTypeSymbol classSymbol,
      ImmutableArray<IPropertySymbol> relationProperties,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      var relations = ImmutableArray.CreateBuilder<RelationDeclarationModel>();

      foreach (var property in relationProperties)
      {
         var location = property.Locations.FirstOrDefault();

         if (!IsSupportedProperty(property))
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.UnsupportedRelationPropertyShape, location, classSymbol.Name, property.Name));
            continue;
         }

         if (HasAttribute(property, COLUMN_ATTRIBUTE_FULL_NAME))
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.RelationCannotBeAColumn, location, classSymbol.Name, property.Name));
            continue;
         }

         if (!TryGetRelationTarget(property.Type, out var target, out var isToMany))
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.UnsupportedRelationPropertyType, location, classSymbol.Name, property.Name, property.Type.ToDisplayString()));
            continue;
         }

         // A relation is always an outer join, so a relation to one row that promises a value cannot be honoured.
         // Left alone when the file has nullable reference types switched off, where the annotation cannot be written.
         if (!isToMany && property.NullableAnnotation == NullableAnnotation.NotAnnotated)
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.RelationToOneMustBeNullable, location, classSymbol.Name, property.Name));
            continue;
         }

         var foreignKeyPropertyName = property.GetAttributes()
            .First(x => x.AttributeClass?.ToDisplayString() == RELATION_ATTRIBUTE_FULL_NAME)
            .ConstructorArguments.FirstOrDefault().Value as string;

         relations.Add(
            new RelationDeclarationModel(
               property.Name,
               GetFullName(target),
               target.ToDisplayString(),
               foreignKeyPropertyName ?? string.Empty,
               isToMany,
               location
            )
         );
      }

      return relations.ToImmutable();
   }

   /// <summary>
   ///    Reads the relation's target and its cardinality off the property's type, which is the only place either is
   ///    stated.
   /// </summary>
   private static bool TryGetRelationTarget(ITypeSymbol propertyType, out INamedTypeSymbol target, out bool isToMany)
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

   private static string GetFullName(INamedTypeSymbol type)
   {
      return type.ContainingNamespace.IsGlobalNamespace
         ? type.Name
         : $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}";
   }

   private static bool IsPartial(INamedTypeSymbol classSymbol)
   {
      return classSymbol.DeclaringSyntaxReferences
         .Select(x => x.GetSyntax())
         .OfType<ClassDeclarationSyntax>()
         .Any(x => x.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
   }

   private static bool IsSupportedProperty(IPropertySymbol property)
   {
      return !property.IsStatic
             && property.DeclaredAccessibility == Accessibility.Public
             && property.Parameters.Length == 0
             && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
             && property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
   }

   private static bool ShouldValidateProperty(IPropertySymbol property)
   {
      return property.DeclaredAccessibility == Accessibility.Public || HasRelevantAttribute(property);
   }

   private static PropertyDefinitionModel CreatePropertyModel(IPropertySymbol property)
   {
      var columnName = property.GetAttributes()
         .FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == COLUMN_ATTRIBUTE_FULL_NAME)?
         .ConstructorArguments.FirstOrDefault().Value as string;

      return new PropertyDefinitionModel(
         property.Name,
         ToCamelCase(property.Name),
         property.Type.ToDisplayString(_typeDisplayFormat),
         string.IsNullOrWhiteSpace(columnName) ? ToSnakeCase(property.Name) : columnName!,
         HasAttribute(property, PRIMARY_KEY_ATTRIBUTE_FULL_NAME),
         HasAttribute(property, UNIQUE_ATTRIBUTE_FULL_NAME),
         HasAttribute(property, GENERATED_ATTRIBUTE_FULL_NAME),
         property.Type.IsReferenceType && property.NullableAnnotation != NullableAnnotation.Annotated
      );
   }

   private static bool HasAttribute(IPropertySymbol property, string fullName)
   {
      return property.GetAttributes().Any(x => x.AttributeClass?.ToDisplayString() == fullName);
   }

   private static bool HasRelevantAttribute(IPropertySymbol property)
   {
      return HasAttribute(property, PRIMARY_KEY_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, UNIQUE_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, COLUMN_ATTRIBUTE_FULL_NAME)
             || HasAttribute(property, GENERATED_ATTRIBUTE_FULL_NAME);
   }

   private static bool HasGeneratedTypeNameCollision(INamedTypeSymbol classSymbol, string typeName)
   {
      return classSymbol.ContainingNamespace
         .GetTypeMembers(typeName)
         .Any(type => !CanMergeWithGeneratedType(type));
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

   private static string GetEntityName(string className)
   {
      return className.EndsWith("Table", StringComparison.Ordinal) && className.Length > "Table".Length
         ? className.Substring(0, className.Length - "Table".Length)
         : className;
   }

   private static bool TryParseTableName(string value, out string schemaName, out string tableName)
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
