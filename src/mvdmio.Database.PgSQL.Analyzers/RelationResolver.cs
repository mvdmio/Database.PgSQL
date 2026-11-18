using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class RelationResolver
{
   internal sealed class ResolveResult
   {
      public ResolveResult(ImmutableArray<ResolvedTable> tables, ImmutableArray<Diagnostic> diagnostics)
      {
         Tables = tables;
         Diagnostics = diagnostics;
      }

      public ImmutableArray<ResolvedTable> Tables { get; }

      public ImmutableArray<Diagnostic> Diagnostics { get; }
   }

   public static ResolveResult Resolve(ImmutableArray<TableDefinitionModel> models)
   {
      var byFullName = models.ToImmutableDictionary(x => x.TableClassFullName, StringComparer.Ordinal);
      var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
      var pending = new List<(TableDefinitionModel Model, List<RelationCandidate> Candidates)>(models.Length);

      foreach (var model in models)
      {
         CheckForForgottenConditions(model, byFullName, diagnostics);

         var candidates = new List<RelationCandidate>(model.Relations.Length);

         foreach (var relation in model.Relations)
         {
            if (PairColumns(model, relation, byFullName, diagnostics) is { } candidate)
               candidates.Add(candidate);
         }

         pending.Add((model, candidates));
      }

      DropRelationsWhoseConditionCannotBeCarried(pending, diagnostics);

      var tables = ImmutableArray.CreateBuilder<ResolvedTable>(pending.Count);

      foreach (var (model, candidates) in pending)
      {
         var resolved = ImmutableArray.CreateBuilder<ResolvedRelation>(candidates.Count);

         foreach (var candidate in candidates)
            resolved.Add(candidate.ToResolvedRelation());

         tables.Add(new ResolvedTable(model, resolved.ToImmutable()));
      }

      return new ResolveResult(tables.ToImmutable(), diagnostics.ToImmutable());
   }

   private static RelationCandidate? PairColumns(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      ImmutableDictionary<string, TableDefinitionModel> byFullName,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
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

         return null;
      }

      var joinedKeys = ImmutableArray.CreateBuilder<JoinedKeyPair>(relation.KeyPairs.Length);
      var beforeResolving = diagnostics.Count;

      foreach (var pair in relation.KeyPairs)
      {
         var declaringProperty = model.DataProperties.FirstOrDefault(x => string.Equals(x.PropertyName, pair.DeclaringPropertyName, StringComparison.Ordinal));
         var targetProperty = target.DataProperties.FirstOrDefault(x => string.Equals(x.PropertyName, pair.TargetPropertyName, StringComparison.Ordinal));

         if (declaringProperty is null || targetProperty is null)
         {
            diagnostics.Add(
               Diagnostic.Create(
                  TableRepositoryDiagnostics.RelationKeyIsNotAColumnReference,
                  pair.Location ?? relation.Location,
                  model.TableClassName,
                  relation.PropertyName
               )
            );

            continue;
         }

         joinedKeys.Add(new JoinedKeyPair(declaringProperty, targetProperty));
      }

      if (diagnostics.Count != beforeResolving)
         return null;

      var candidate = new RelationCandidate(model, relation, target, joinedKeys.ToImmutable());

      return CheckKeyPairClaims(candidate, diagnostics) ? candidate : null;
   }

   private static bool CheckKeyPairClaims(RelationCandidate candidate, ImmutableArray<Diagnostic>.Builder diagnostics)
   {
      var relation = candidate.Relation;
      var beforeChecking = diagnostics.Count;

      foreach (var pair in candidate.JoinedKeys)
      {
         if (pair.ThisKey.Nullability.IsNotNull || pair.TargetKey.Nullability.IsNotNull)
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationKeyPairBothNullable,
               relation.Location,
               candidate.Model.TableClassName,
               relation.PropertyName,
               pair.ThisKey.PropertyName,
               candidate.Target.TableClassName,
               pair.TargetKey.PropertyName
            )
         );
      }

      if (diagnostics.Count != beforeChecking)
         return false;

      if (!relation.IsToMany && !PairedColumnsClaimUniqueness(candidate.Target, candidate.JoinedKeys))
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationToOneRowMayReachSeveral,
               relation.Location,
               candidate.Model.TableClassName,
               relation.PropertyName,
               candidate.Target.TableClassName
            )
         );
      }

      CheckTenancyPairing(candidate, diagnostics);

      return true;
   }

   private static bool PairedColumnsClaimUniqueness(TableDefinitionModel target, ImmutableArray<JoinedKeyPair> joinedKeys)
   {
      var pairedTargetColumns = new HashSet<PropertyDefinitionModel>(joinedKeys.Select(x => x.TargetKey));

      if (!target.PrimaryKeys.IsEmpty && target.PrimaryKeys.All(pairedTargetColumns.Contains))
         return true;

      return pairedTargetColumns.Any(x => x.IsUnique);
   }

   private static void DropRelationsWhoseConditionCannotBeCarried(
      List<(TableDefinitionModel Model, List<RelationCandidate> Candidates)> pending,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      bool droppedAny;

      do
      {
         droppedAny = false;

         var memberNamesByTable = pending.ToDictionary(
            x => x.Model.TableClassFullName,
            x => MemberNames(x.Model, x.Candidates),
            StringComparer.Ordinal
         );

         foreach (var (_, candidates) in pending)
         {
            for (var index = candidates.Count - 1; index >= 0; index--)
            {
               if (ConditionCanBeCarried(candidates[index], memberNamesByTable, diagnostics))
                  continue;

               candidates.RemoveAt(index);
               droppedAny = true;
            }
         }
      }
      while (droppedAny);
   }

   private static bool ConditionCanBeCarried(
      RelationCandidate candidate,
      Dictionary<string, HashSet<string>> memberNamesByTable,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      var condition = candidate.Relation.Condition;

      if (condition is null)
         return true;

      var beforeChecking = diagnostics.Count;

      foreach (var memberAccess in condition.MemberAccesses)
      {
         var owner = memberAccess.IsDeclaringSide ? candidate.Model : candidate.Target;

         if (memberNamesByTable.TryGetValue(owner.TableClassFullName, out var members) && members.Contains(memberAccess.MemberName))
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationConditionCannotBeCarried,
               memberAccess.Location ?? candidate.Relation.Location,
               candidate.Model.TableClassName,
               candidate.Relation.PropertyName,
               owner.TableClassName,
               memberAccess.MemberName
            )
         );
      }

      return diagnostics.Count == beforeChecking;
   }

   private static HashSet<string> MemberNames(TableDefinitionModel model, List<RelationCandidate> candidates)
   {
      var names = new HashSet<string>(StringComparer.Ordinal);

      foreach (var property in model.DataProperties)
         names.Add(property.PropertyName);

      foreach (var candidate in candidates)
         names.Add(candidate.Relation.PropertyName);

      return names;
   }

   private static void CheckTenancyPairing(RelationCandidate candidate, ImmutableArray<Diagnostic>.Builder diagnostics)
   {
      CheckTenancySide(candidate, candidate.Model, candidate.Target, isDeclaringSide: true, diagnostics);
      CheckTenancySide(candidate, candidate.Target, candidate.Model, isDeclaringSide: false, diagnostics);
   }

   private static void CheckTenancySide(
      RelationCandidate candidate,
      TableDefinitionModel owner,
      TableDefinitionModel otherSide,
      bool isDeclaringSide,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      foreach (var tenancyColumn in owner.TenancyColumns)
      {
         var pair = candidate.JoinedKeys.FirstOrDefault(x => ReferenceEquals(isDeclaringSide ? x.ThisKey : x.TargetKey, tenancyColumn));
         var counterpart = pair is null ? null : isDeclaringSide ? pair.TargetKey : pair.ThisKey;

         if (counterpart is { IsTenancy: true })
            continue;

         if (counterpart is null && otherSide.TenancyColumns.IsEmpty)
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationCouldReachAcrossTenants,
               candidate.Relation.Location,
               candidate.Model.TableClassName,
               candidate.Relation.PropertyName,
               tenancyColumn.PropertyName
            )
         );
      }
   }

   private static void CheckForForgottenConditions(
      TableDefinitionModel model,
      ImmutableDictionary<string, TableDefinitionModel> byFullName,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      var byShape = model.Relations
         .Where(x => byFullName.ContainsKey(x.TargetClassFullName))
         .GroupBy(
            x => string.Join("|", x.KeyPairs.Select(pair => pair.DeclaringPropertyName).OrderBy(name => name, StringComparer.Ordinal)),
            StringComparer.Ordinal
         );

      foreach (var group in byShape)
      {
         if (group.Count() < 2 || group.All(x => x.Condition is null))
            continue;

         foreach (var unconditioned in group.Where(x => x.Condition is null))
         {
            diagnostics.Add(
               Diagnostic.Create(
                  TableRepositoryDiagnostics.RelationMayResolveEveryKind,
                  unconditioned.Location,
                  model.TableClassName,
                  unconditioned.PropertyName
               )
            );
         }
      }
   }

   internal static string QualifyTypeName(string namespaceName, string typeName)
   {
      return string.IsNullOrWhiteSpace(namespaceName) ? $"global::{typeName}" : $"global::{namespaceName}.{typeName}";
   }
}
