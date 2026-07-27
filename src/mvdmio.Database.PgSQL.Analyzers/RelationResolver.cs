using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    Resolves every declared relation against the set of parsed table definitions, which is the only stage that sees
///    more than one table at a time.
/// </summary>
/// <remarks>
///    Relations are one-directional and are never paired with the declaration facing the other way: each is
///    self-sufficient, the provider's own association metadata is one-directional, and pairing would buy a matching
///    rule and ambiguity diagnostics for nothing at translation time. A cycle is therefore ordinary — a relation to a
///    parent alongside a relation to that parent's children already is one.
/// </remarks>
internal static class RelationResolver
{
   internal sealed class ResolveResult
   {
      public ResolveResult(ImmutableArray<ResolvedTable> tables, ImmutableArray<Diagnostic> diagnostics)
      {
         Tables = tables;
         Diagnostics = diagnostics;
      }

      /// <summary>Every table definition, each paired with the relations of its own that resolved.</summary>
      public ImmutableArray<ResolvedTable> Tables { get; }

      public ImmutableArray<Diagnostic> Diagnostics { get; }
   }

   public static ResolveResult Resolve(ImmutableArray<TableDefinitionModel> models)
   {
      var byFullName = models.ToImmutableDictionary(x => x.TableClassFullName, StringComparer.Ordinal);
      var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
      var tables = ImmutableArray.CreateBuilder<ResolvedTable>(models.Length);

      foreach (var model in models)
      {
         var relations = ImmutableArray.CreateBuilder<ResolvedRelation>(model.Relations.Length);

         foreach (var relation in model.Relations)
         {
            if (TryResolve(model, relation, byFullName, diagnostics, out var result))
               relations.Add(result);
         }

         tables.Add(new ResolvedTable(model, relations.ToImmutable()));
      }

      return new ResolveResult(tables.ToImmutable(), diagnostics.ToImmutable());
   }

   private static bool TryResolve(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      ImmutableDictionary<string, TableDefinitionModel> byFullName,
      ImmutableArray<Diagnostic>.Builder diagnostics,
      out ResolvedRelation resolved
   )
   {
      resolved = null!;

      if (!byFullName.TryGetValue(relation.TargetClassFullName, out var target))
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationTargetIsNotATableDefinition,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               relation.TargetTypeDisplayName
            )
         );

         return false;
      }

      // Cardinality decides one thing: which side holds the foreign key and which holds the primary key it joins. A
      // relation to one row is resolved through a foreign key on the declaring type, one to many through a foreign key
      // on the target. The far end is always the other side's primary key, because exactly one non-composite primary
      // key per table definition is already enforced.
      var (foreignKeyOwner, primaryKeyOwner) = relation.IsToMany ? (target, model) : (model, target);
      var foreignKey = foreignKeyOwner.DataProperties.FirstOrDefault(x => string.Equals(x.PropertyName, relation.ForeignKeyPropertyName, StringComparison.Ordinal));

      if (foreignKey is null)
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationForeignKeyNotFound,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               relation.ForeignKeyPropertyName,
               foreignKeyOwner.TableClassName
            )
         );

         return false;
      }

      var primaryKey = primaryKeyOwner.PrimaryKey;

      if (!CanJoin(foreignKey.TypeName, primaryKey.TypeName))
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationForeignKeyTypeMismatch,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               foreignKey.PropertyName,
               foreignKey.TypeName,
               primaryKey.PropertyName,
               primaryKey.TypeName
            )
         );

         return false;
      }

      resolved = new ResolvedRelation(
         relation.PropertyName,
         relation.IsToMany,
         QualifyTypeName(target.NamespaceName, target.DataTypeName),
         foreignKey,
         primaryKey
      );

      return true;
   }

   /// <summary>
   ///    Whether a foreign key can join a primary key. Nullability is ignored: a nullable foreign key joining a
   ///    non-nullable primary key is the ordinary case, and is exactly what makes the relation an outer join.
   /// </summary>
   private static bool CanJoin(string foreignKeyTypeName, string primaryKeyTypeName)
   {
      return string.Equals(foreignKeyTypeName.TrimEnd('?'), primaryKeyTypeName.TrimEnd('?'), StringComparison.Ordinal);
   }

   private static string QualifyTypeName(string namespaceName, string typeName)
   {
      return string.IsNullOrWhiteSpace(namespaceName) ? $"global::{typeName}" : $"global::{namespaceName}.{typeName}";
   }
}

/// <summary>
///    A table definition together with the relations of its own that resolved. Pairing them keeps every consumer from
///    having to look one up by the other.
/// </summary>
internal sealed class ResolvedTable
{
   public ResolvedTable(TableDefinitionModel model, ImmutableArray<ResolvedRelation> relations)
   {
      Model = model;
      Relations = relations;
   }

   public TableDefinitionModel Model { get; }
   public ImmutableArray<ResolvedRelation> Relations { get; }
}

/// <summary>
///    A relation whose target and keys have been resolved, carrying everything the emitted mapping and the mirrored
///    property need.
/// </summary>
internal sealed class ResolvedRelation
{
   public ResolvedRelation(
      string propertyName,
      bool isToMany,
      string targetDataTypeName,
      PropertyDefinitionModel foreignKey,
      PropertyDefinitionModel primaryKey
   )
   {
      PropertyName = propertyName;
      IsToMany = isToMany;
      TargetDataTypeName = targetDataTypeName;

      // A relation always joins its foreign key to a primary key. Which of the two is on the declaring side is the
      // whole of what the cardinality decides, so it is decided here and nowhere else.
      ThisKey = isToMany ? primaryKey : foreignKey;
      TargetKey = isToMany ? foreignKey : primaryKey;
   }

   public string PropertyName { get; }
   public bool IsToMany { get; }

   /// <summary>The globally qualified generated data type on the other side of the relation.</summary>
   public string TargetDataTypeName { get; }

   /// <summary>The key on the declaring side of the relation.</summary>
   public PropertyDefinitionModel ThisKey { get; }

   /// <summary>The key on the target side of the relation.</summary>
   public PropertyDefinitionModel TargetKey { get; }
}
