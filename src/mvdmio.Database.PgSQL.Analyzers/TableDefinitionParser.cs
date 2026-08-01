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

      // The relation split is type-driven: a property typed as a class deriving from RelationDefinition<,>, or a
      // supported collection of one, is a relation on its own. [Relation] still opts a property out of column
      // mapping too, which is what the old attribute-argument form needs, since its target is the property's own
      // type with nothing further to read.
      var relationProperties = allProperties.Where(x => TableDefinitionSymbols.IsRelationProperty(x, context.SemanticModel.Compilation)).ToImmutableArray();
      var columnCandidates = allProperties.Where(x => !TableDefinitionSymbols.IsRelationProperty(x, context.SemanticModel.Compilation)).ToImmutableArray();

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

      // Reports every tenancy column matching isRefused, and answers whether the table has to be abandoned. Both
      // refusals below abandon it, the same as a nullable key member does: generating the table anyway would emit
      // precisely the unguarded surface a tenancy column exists to remove.
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

      // A null tenant matches no row, so every generated member would return nothing. IsDeclaredNotNull already folds
      // the property's type and a Null = true claim into one answer (a dropped contradiction falls back to the type),
      // so checking it here catches both without checking each separately. A key member that is also nullable is
      // already caught above and this table already abandoned, so a property malformed both ways reports one clear
      // reason rather than two.
      if (RefusesTenancyColumns(x => !x.IsDeclaredNotNull, TableRepositoryDiagnostics.NullableTenancyColumn))
         return new ParseResult(null, diagnostics.ToImmutable());

      // A generated column is on no command type, so there is no property to make required, and the developer would
      // learn that at run time instead of build time.
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

      // A tenancy column is excluded here whether or not it is a key member: the generated update never assigns it, so a
      // row cannot change tenant through the generated surface. It still reaches the update command type — the model
      // derives TableDefinitionModel.UpdateProperties by widening this set back out with the key and the tenancy
      // columns outside it, because the WHERE clause needs their values even though the SET list never does.
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

      // Declaration order, same as the primary key above: it fixes the parameter order of every generated member that
      // takes a tenancy column's value.
      var tenancyColumns = properties.Where(x => x.IsTenancy).ToImmutableArray();

      var relations = ParseRelations(classSymbol, relationProperties, context.SemanticModel.Compilation, diagnostics);
      var accessibility = classSymbol.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
      // Named throughout: four of these are same-typed property collections, so a transposition would compile and only
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
         lookupProperties: lookupProperties,
         mutableUpdateProperties: mutableUpdateProperties,
         tenancyColumns: tenancyColumns,
         relations: relations
      );

      return new ParseResult(model, diagnostics.ToImmutable());
   }

   /// <summary>
   ///    Everything one column's storage earns: an unwritable property type, a claim that cannot be honoured for it, a
   ///    claim only one of the two surfaces can carry, and a type neither the claim nor a registered conversion covers.
   /// </summary>
   /// <remarks>
   ///    Abandons nothing. Each of these leaves every generated signature well-defined, so reporting and carrying on
   ///    gives the consumer the one message describing the mistake rather than that message buried under type-not-found
   ///    errors from everything naming a type that was never emitted.
   ///    <para>
   ///       <paramref name="properties" /> is <paramref name="mappedProperties" /> in the same order, one model per
   ///       symbol, so the two are read by index: the diagnostics need the symbol for its location and the type name the
   ///       developer wrote, and the model for what its storage settled.
   ///    </para>
   /// </remarks>
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

            // The unmappable-type warning would only add noise here: its advice is to register a conversion, and the
            // error above is that there is no PostgreSQL type to convert to.
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

   /// <remarks>
   ///    Only what one table can decide on its own is checked here: the property's shape, its type, and whether it
   ///    contradicts a column attribute. Whether the target is a table definition and whether the named foreign key
   ///    (or, for the type-driven form, a key pair's target side) resolves to a mapped column are cross-table
   ///    questions, answered once every table has been parsed. A relation that fails a check here is dropped and the
   ///    rest of the table still generates.
   /// </remarks>
   private static ImmutableArray<RelationDeclarationModel> ParseRelations(
      INamedTypeSymbol classSymbol,
      ImmutableArray<IPropertySymbol> relationProperties,
      Compilation compilation,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      var relations = ImmutableArray.CreateBuilder<RelationDeclarationModel>();

      foreach (var property in relationProperties)
      {
         var location = property.Locations.FirstOrDefault();

         if (!TableDefinitionSymbols.IsSupportedRelationPropertyShape(property))
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.UnsupportedRelationPropertyShape, location, classSymbol.Name, property.Name));
            continue;
         }

         if (TableDefinitionSymbols.HasAttribute(property, TableDefinitionSymbols.COLUMN_ATTRIBUTE_FULL_NAME))
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.RelationCannotBeAColumn, location, classSymbol.Name, property.Name));
            continue;
         }

         if (!TableDefinitionSymbols.TryGetRelationTarget(property.Type, compilation, out var target, out var isToMany, out var relationDefinition, out var declaringTypeArgument))
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

         if (relationDefinition is not null)
         {
            if (!TryParseRelationDefinition(classSymbol, property, location, target, isToMany, relationDefinition, declaringTypeArgument, compilation, diagnostics, out var definitionRelation))
               continue;

            relations.Add(definitionRelation);
            continue;
         }

         // The old attribute-argument form: the target is the property's own type (or collection element type),
         // and the foreign key is named on the [Relation] attribute rather than stated as key pairs.
         var relationAttribute = TableDefinitionSymbols.RelationAttributeOf(property);

         relations.Add(
            new RelationDeclarationModel(
               property.Name,
               TableDefinitionSymbols.GetFullName(target),
               target.ToDisplayString(),
               TableDefinitionSymbols.GetForeignKeyPropertyNames(relationAttribute),
               keyPairs: null,
               isToMany,
               location
            )
         );
      }

      return relations.ToImmutable();
   }

   /// <summary>
   ///    Parses a relation declared as a class deriving from <c>RelationDefinition&lt;TDeclaring, TTarget&gt;</c>:
   ///    checks that <c>TDeclaring</c> is the table the property is declared on, and reads the pairs off the
   ///    definition's <c>Keys</c> override.
   /// </summary>
   private static bool TryParseRelationDefinition(
      INamedTypeSymbol classSymbol,
      IPropertySymbol property,
      Location? location,
      INamedTypeSymbol target,
      bool isToMany,
      INamedTypeSymbol relationDefinition,
      INamedTypeSymbol? declaringTypeArgument,
      Compilation compilation,
      ImmutableArray<Diagnostic>.Builder diagnostics,
      out RelationDeclarationModel relation
   )
   {
      relation = null!;

      if (declaringTypeArgument is null || !SymbolEqualityComparer.Default.Equals(declaringTypeArgument, classSymbol))
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationDeclaringTableMismatch,
               location,
               classSymbol.Name,
               property.Name,
               declaringTypeArgument?.ToDisplayString() ?? "?"
            )
         );

         return false;
      }

      var keyPairs = TableDefinitionSymbols.ReadRelationKeyPairDeclarations(relationDefinition, compilation);

      if (keyPairs.IsEmpty)
      {
         diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.RelationStatesNoKeys, location, classSymbol.Name, property.Name));
         return false;
      }

      var invalidPairs = keyPairs.Where(x => x.DeclaringPropertyName is null || x.TargetPropertyName is null).ToImmutableArray();

      if (!invalidPairs.IsEmpty)
      {
         foreach (var invalidPair in invalidPairs)
         {
            diagnostics.Add(
               Diagnostic.Create(TableRepositoryDiagnostics.RelationKeyIsNotAColumnReference, invalidPair.Location ?? location, classSymbol.Name, property.Name)
            );
         }

         return false;
      }

      var condition = TableDefinitionSymbols.ReadRelationCondition(relationDefinition, compilation);

      relation = new RelationDeclarationModel(
         property.Name,
         TableDefinitionSymbols.GetFullName(target),
         target.ToDisplayString(),
         ImmutableArray<string>.Empty,
         keyPairs,
         isToMany,
         location,
         condition
      );

      return true;
   }
}
