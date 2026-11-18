using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

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

      var compilation = context.SemanticModel.Compilation;

      var bySide = classSymbol.GetMembers()
         .OfType<IPropertySymbol>()
         .ToLookup(x => TableDefinitionSymbols.IsRelationProperty(x, compilation));

      var relationProperties = bySide[true].ToImmutableArray();
      var columnCandidates = bySide[false].ToImmutableArray();

      foreach (var property in columnCandidates.Where(x => TableDefinitionSymbols.HasAttribute(x, TableDefinitionSymbols.RELATION_ATTRIBUTE_FULL_NAME)))
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.RelationAttributeOnNonRelationProperty,
            property.Locations.FirstOrDefault() ?? classSyntax.Identifier.GetLocation(),
            classSymbol.Name,
            property.Name
         ));
      }

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

      var properties = mappedProperties
         .Select(TableDefinitionSymbols.CreatePropertyModel)
         .ToImmutableArray();

      ReportStorageDiagnostics(classSymbol, classSyntax, mappedProperties, properties, diagnostics);

      foreach (var property in properties.Where(x => x.Nullability.Contradiction is not null))
      {
         diagnostics.Add(Diagnostic.Create(
            TableRepositoryDiagnostics.ContradictoryColumnNullability,
            TableDefinitionSymbols.PropertyLocation(mappedProperties, property, classSyntax),
            classSymbol.Name,
            property.PropertyName,
            property.Nullability.Contradiction
         ));
      }

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

      var nullableKeyMembers = primaryKeys.Where(x => x.TypeCanHoldNull).ToImmutableArray();
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

      bool RefusesTenancyColumns(Func<PropertyDefinitionModel, bool> isRefused, DiagnosticDescriptor descriptor)
      {
         var refused = properties.Where(x => x.IsTenancy && isRefused(x)).ToImmutableArray();

         foreach (var tenancyColumn in refused)
         {
            diagnostics.Add(Diagnostic.Create(
               descriptor,
               TableDefinitionSymbols.PropertyLocation(mappedProperties, tenancyColumn, classSyntax),
               classSymbol.Name,
               tenancyColumn.PropertyName
            ));
         }

         return !refused.IsEmpty;
      }

      if (RefusesTenancyColumns(x => !x.Nullability.IsNotNull, TableRepositoryDiagnostics.NullableTenancyColumn))
         return new ParseResult(null, diagnostics.ToImmutable());

      if (RefusesTenancyColumns(x => x.IsGenerated, TableRepositoryDiagnostics.GeneratedTenancyColumn))
         return new ParseResult(null, diagnostics.ToImmutable());

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

      var mutableUpdateProperties = properties.Where(x => !x.IsPrimaryKey && !x.IsGenerated && !x.IsTenancy).ToImmutableArray();
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

      var tenancyColumns = properties.Where(x => x.IsTenancy).ToImmutableArray();

      var relations = RelationDeclarationParser.ParseRelations(classSymbol, relationProperties, compilation, diagnostics);
      var accessibility = classSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
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
         lookupProperties: lookupProperties,
         mutableUpdateProperties: mutableUpdateProperties,
         tenancyColumns: tenancyColumns,
         relations: relations
      );

      return new ParseResult(model, diagnostics.ToImmutable());
   }

   private static void ReportStorageDiagnostics(
      INamedTypeSymbol classSymbol,
      ClassDeclarationSyntax classSyntax,
      ImmutableArray<IPropertySymbol> mappedProperties,
      ImmutableArray<PropertyDefinitionModel> properties,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      for (var index = 0; index < properties.Length; index++)
      {
         var property = properties[index];
         var symbol = mappedProperties[index];
         var storage = property.Storage;
         var location = symbol.Locations.FirstOrDefault() ?? classSyntax.Identifier.GetLocation();

         if (storage.IsUnwritableType)
         {
            diagnostics.Add(Diagnostic.Create(
               TableRepositoryDiagnostics.UnwritablePropertyType,
               location,
               classSymbol.Name,
               property.PropertyName,
               symbol.Type.ToDisplayString(),
               TableRepositoryDiagnostics.WRITABLE_ALTERNATIVES_FOR_UNSIGNED_INTEGERS
            ));

            continue;
         }

         if (storage.RefusedClaim is not null)
         {
            diagnostics.Add(Diagnostic.Create(
               TableRepositoryDiagnostics.RefusedStorageClaim,
               location,
               classSymbol.Name,
               property.PropertyName,
               storage.RefusedClaim,
               symbol.Type.ToDisplayString(),
               storage.RefusalAlternatives
            ));
         }
         else if (storage.HasNoQueryRepresentation)
         {
            diagnostics.Add(Diagnostic.Create(
               TableRepositoryDiagnostics.UnrepresentableStorageClaim,
               location,
               classSymbol.Name,
               property.PropertyName,
               storage.MappedAs
            ));
         }

         if (!QueryMappableTypes.IsMappable(symbol.Type, storage))
         {
            diagnostics.Add(Diagnostic.Create(
               TableRepositoryDiagnostics.UnmappableQueryPropertyType,
               location,
               classSymbol.Name,
               property.PropertyName,
               symbol.Type.ToDisplayString()
            ));
         }
      }
   }
}
