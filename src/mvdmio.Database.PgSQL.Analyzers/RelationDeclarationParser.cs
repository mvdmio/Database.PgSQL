using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Turns the relation properties of one table definition into declaration models, reporting the mistakes a single
///    table can see for itself.
/// </summary>
/// <remarks>
///    Separated from <see cref="TableDefinitionParser" /> because a relation is the one member whose declaration lives
///    somewhere other than the property: reading it means following the property's type to a
///    <c>RelationDefinition&lt;,&gt;</c> class and reading that class's overrides. Every check here is one a single
///    table can answer; the cross-table ones — whether the target is a table definition at all, and whether each key
///    pair resolves to a mapped column — belong to <see cref="RelationResolver" />.
/// </remarks>
internal static class RelationDeclarationParser
{
   /// <remarks>
   ///    Only what one table can decide on its own is checked here: the property's shape, its type, and whether it
   ///    contradicts a column attribute. Whether the target is a table definition and whether a key pair's target
   ///    side resolves to a mapped column are cross-table questions, answered once every table has been parsed. A
   ///    relation that fails a check here is dropped and the rest of the table still generates.
   /// </remarks>
   public static ImmutableArray<RelationDeclarationModel> ParseRelations(
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

         if (TableDefinitionSymbols.ReadRelationPropertyShape(property.Type, compilation) is not { } shape)
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.UnsupportedRelationPropertyType, location, classSymbol.Name, property.Name, property.Type.ToDisplayString()));
            continue;
         }

         // A relation is always an outer join, so a relation to one row that promises a value cannot be honoured.
         // Left alone when the file has nullable reference types switched off, where the annotation cannot be written.
         if (!shape.IsToMany && property.NullableAnnotation == NullableAnnotation.NotAnnotated)
         {
            diagnostics.Add(Diagnostic.Create(TableRepositoryDiagnostics.RelationToOneMustBeNullable, location, classSymbol.Name, property.Name));
            continue;
         }

         if (!TryParseRelationDefinition(classSymbol, property, location, shape, compilation, diagnostics, out var definitionRelation))
            continue;

         relations.Add(definitionRelation);
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
      RelationPropertyShape shape,
      Compilation compilation,
      ImmutableArray<Diagnostic>.Builder diagnostics,
      out RelationDeclarationModel relation
   )
   {
      relation = null!;

      if (shape.DeclaringTypeArgument is null || !SymbolEqualityComparer.Default.Equals(shape.DeclaringTypeArgument, classSymbol))
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationDeclaringTableMismatch,
               location,
               classSymbol.Name,
               property.Name,
               shape.DeclaringTypeArgument?.ToDisplayString() ?? "?"
            )
         );

         return false;
      }

      var keyPairs = RelationDefinitionReader.ReadRelationKeyPairDeclarations(shape.RelationDefinition, compilation);

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

      var condition = RelationDefinitionReader.ReadRelationCondition(shape.RelationDefinition, compilation);

      relation = new RelationDeclarationModel(
         property.Name,
         TableDefinitionSymbols.GetFullName(shape.Target),
         shape.Target.ToDisplayString(),
         keyPairs,
         shape.IsToMany,
         location,
         condition
      );

      return true;
   }
}

/// <summary>
///    What a relation property's type states: the two Table definitions its <c>RelationDefinition&lt;,&gt;</c> class
///    names, the definition class itself, and whether the property reaches one row or many.
/// </summary>
/// <remarks>
///    One type rather than four out-parameters, because all four are read from the same base type in one step and
///    every caller needs all of them. <see cref="DeclaringTypeArgument" /> is nullable where the others are not: a
///    <c>TDeclaring</c> that is not a named type is the mistake <c>PGSQL0028</c> reports, so it has to survive
///    reading in order to be reported.
/// </remarks>
internal sealed class RelationPropertyShape
{
   public RelationPropertyShape(
      INamedTypeSymbol target,
      bool isToMany,
      INamedTypeSymbol relationDefinition,
      INamedTypeSymbol? declaringTypeArgument
   )
   {
      Target = target;
      IsToMany = isToMany;
      RelationDefinition = relationDefinition;
      DeclaringTypeArgument = declaringTypeArgument;
   }

   /// <summary>The <c>TTarget</c> type argument — the Table definition the relation reaches.</summary>
   public INamedTypeSymbol Target { get; }

   public bool IsToMany { get; }

   /// <summary>The relation definition class itself, whose <c>Keys</c> and <c>Condition</c> overrides are read next.</summary>
   public INamedTypeSymbol RelationDefinition { get; }

   /// <summary>The <c>TDeclaring</c> type argument, which must be the table the relation property is declared on.</summary>
   public INamedTypeSymbol? DeclaringTypeArgument { get; }
}
