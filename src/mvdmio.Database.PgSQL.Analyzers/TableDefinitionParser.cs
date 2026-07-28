using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Decides whether one table definition is usable, and produces the model every generated type derives from.
/// </summary>
/// <remarks>
///    What the symbols say is read by <see cref="TableDefinitionSymbols" />; this file decides what each fact earns.
///    Every check up to and including the relations either abandons the table — a malformed key, name or column leaves
///    every generated signature undefined — or, for a relation-level problem, drops just that relation.
/// </remarks>
internal static class TableDefinitionParser
{
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

      var tableAttribute = classSymbol.GetAttributes().FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == TableDefinitionSymbols.TABLE_ATTRIBUTE_FULL_NAME);
      var tableName = tableAttribute?.ConstructorArguments.FirstOrDefault().Value as string;

      if (string.IsNullOrWhiteSpace(tableName) || !TableDefinitionSymbols.TryParseTableName(tableName!, out var schemaName, out var unqualifiedTableName))
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.InvalidTableName,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            tableName ?? string.Empty
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      if (!TableDefinitionSymbols.IsPartial(classSymbol))
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
      var relationProperties = allProperties.Where(x => TableDefinitionSymbols.HasAttribute(x, TableDefinitionSymbols.RELATION_ATTRIBUTE_FULL_NAME)).ToImmutableArray();
      var columnCandidates = allProperties.Where(x => !TableDefinitionSymbols.HasAttribute(x, TableDefinitionSymbols.RELATION_ATTRIBUTE_FULL_NAME)).ToImmutableArray();

      var invalidProperties = columnCandidates
         .Where(TableDefinitionSymbols.ShouldValidateProperty)
         .Where(x => !TableDefinitionSymbols.IsSupportedProperty(x))
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

      var mappedProperties = columnCandidates.Where(TableDefinitionSymbols.IsSupportedProperty).ToImmutableArray();

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
         .Select(TableDefinitionSymbols.CreatePropertyModel)
         .ToImmutableArray();

      // Abandons nothing: the claim has already been dropped, so every generated signature is still well-defined and
      // the consumer reads this one error rather than type-not-found errors from everything naming a missing type.
      foreach (var property in properties.Where(x => x.NullabilityContradiction is not null))
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.ContradictoryColumnNullability,
            TableDefinitionSymbols.PropertyLocation(mappedProperties, property, classSyntax),
            classSymbol.Name,
            property.PropertyName,
            property.NullabilityContradiction
         ));
      }

      // Declaration order, because GetMembers returns source order, and that order is the key order the generated
      // lookup, delete and update all count on.
      var primaryKeys = properties.Where(x => x.IsPrimaryKey).ToImmutableArray();
      if (primaryKeys.IsEmpty)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.TableClassMustHaveAPrimaryKey,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var nullableKeyMembers = primaryKeys.Where(x => x.IsNullable).ToImmutableArray();
      if (!nullableKeyMembers.IsEmpty)
      {
         foreach (var keyMember in nullableKeyMembers)
         {
            diagnostics.Add(Diagnostic.Create(
               TableRepositoryDiagnostics.NullablePrimaryKeyProperty,
               TableDefinitionSymbols.PropertyLocation(mappedProperties, keyMember, classSyntax),
               classSymbol.Name,
               keyMember.PropertyName,
               keyMember.TypeName
            ));
         }

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

      // The primary key is not a lookup property any more: its lookup and delete are named after the key rather than
      // after a property, so that every repository names them the same way.
      var lookupProperties = properties.Where(x => x.IsUnique).ToImmutableArray();
      var duplicateLookup = lookupProperties
         .Select(x => TableRepositorySourceBuilder.LookupMethodName(x))
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

      var reservedLookup = lookupProperties.FirstOrDefault(
         x => string.Equals(TableRepositorySourceBuilder.LookupMethodName(x), TableRepositorySourceBuilder.PRIMARY_KEY_LOOKUP_METHOD_NAME, StringComparison.Ordinal)
      );

      if (reservedLookup is not null)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.GeneratedNameCollision,
            TableDefinitionSymbols.PropertyLocation(mappedProperties, reservedLookup, classSyntax),
            classSymbol.Name,
            TableRepositorySourceBuilder.LookupMethodName(reservedLookup),
            TableRepositoryDiagnostics.COLLISION_REASON_PRIMARY_KEY_LOOKUP
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

      var entityName = TableDefinitionSymbols.GetEntityName(classSymbol.Name);
      var generatedTypeNames = new[]
      {
         $"{entityName}Data",
         $"Create{entityName}Command",
         $"Update{entityName}Command",
         $"I{entityName}Repository",
         $"{entityName}Repository"
      };

      var generatedNameCollision = generatedTypeNames.FirstOrDefault(typeName => TableDefinitionSymbols.HasGeneratedTypeNameCollision(classSymbol, typeName));
      if (generatedNameCollision is not null)
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.GeneratedNameCollision,
            classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            generatedNameCollision,
            TableRepositoryDiagnostics.COLLISION_REASON_NON_PARTIAL_TYPE
         ));

         return new ParseResult(null, diagnostics.ToImmutable());
      }

      var relations = ParseRelations(classSymbol, relationProperties, diagnostics);
      var accessibility = classSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
      // Named throughout: five of these are same-typed property collections, so a transposition would compile and only
      // show up as wrong generated SQL.
      var model = new TableDefinitionModel(
         namespaceName: classSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : classSymbol.ContainingNamespace.ToDisplayString(),
         accessibility: accessibility,
         tableClassName: classSymbol.Name,
         tableClassFullName: TableDefinitionSymbols.GetFullName(classSymbol),
         entityName: entityName,
         dataTypeName: $"{entityName}Data",
         createCommandTypeName: $"Create{entityName}Command",
         updateCommandTypeName: $"Update{entityName}Command",
         repositoryInterfaceTypeName: $"I{entityName}Repository",
         repositoryTypeName: $"{entityName}Repository",
         schemaName: schemaName,
         tableName: unqualifiedTableName,
         primaryKeys: primaryKeys,
         dataProperties: properties,
         createProperties: properties.Where(x => !x.IsGenerated).ToImmutableArray(),
         updateProperties: primaryKeys.AddRange(mutableUpdateProperties),
         lookupProperties: lookupProperties,
         mutableUpdateProperties: mutableUpdateProperties,
         relations: relations
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

         if (!TableDefinitionSymbols.IsSupportedProperty(property))
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.UnsupportedRelationPropertyShape, location, classSymbol.Name, property.Name));
            continue;
         }

         if (TableDefinitionSymbols.HasAttribute(property, TableDefinitionSymbols.COLUMN_ATTRIBUTE_FULL_NAME))
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.RelationCannotBeAColumn, location, classSymbol.Name, property.Name));
            continue;
         }

         if (!TableDefinitionSymbols.TryGetRelationTarget(property.Type, out var target, out var isToMany))
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

         var relationAttribute = TableDefinitionSymbols.RelationAttributeOf(property);

         relations.Add(
            new RelationDeclarationModel(
               property.Name,
               TableDefinitionSymbols.GetFullName(target),
               target.ToDisplayString(),
               TableDefinitionSymbols.GetForeignKeyPropertyNames(relationAttribute),
               isToMany,
               location
            )
         );
      }

      return relations.ToImmutable();
   }
}
