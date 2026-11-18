using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class RelationDeclarationParser
{
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

   public INamedTypeSymbol Target { get; }

   public bool IsToMany { get; }

   public INamedTypeSymbol RelationDefinition { get; }

   public INamedTypeSymbol? DeclaringTypeArgument { get; }
}
