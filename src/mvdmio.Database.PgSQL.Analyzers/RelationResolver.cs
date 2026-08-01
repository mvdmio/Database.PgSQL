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

   /// <remarks>
   ///    Two passes, because the two stages need different things. Pairing a relation's columns needs only the two
   ///    tables it names, so it happens per table as the models are walked. Checking a Relation condition needs to
   ///    know which relations <em>survived</em> that first pass, because a condition may reach through another
   ///    relation and the generated data type mirrors only the relations that resolved — checking against the
   ///    declared ones would let a condition touching a dropped relation pass here and fail inside generated source
   ///    instead, which is the one failure the check exists to prevent.
   /// </remarks>
   public static ResolveResult Resolve(ImmutableArray<TableDefinitionModel> models)
   {
      var byFullName = models.ToImmutableDictionary(x => x.TableClassFullName, StringComparer.Ordinal);
      var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
      var candidatesByTable = new Dictionary<string, List<RelationCandidate>>(StringComparer.Ordinal);

      foreach (var model in models)
      {
         CheckForForgottenConditions(model, byFullName, diagnostics);

         var candidates = new List<RelationCandidate>(model.Relations.Length);

         foreach (var relation in model.Relations)
         {
            if (TryPairColumns(model, relation, byFullName, diagnostics, out var candidate))
               candidates.Add(candidate);
         }

         candidatesByTable[model.TableClassFullName] = candidates;
      }

      DropRelationsWhoseConditionCannotBeCarried(models, candidatesByTable, diagnostics);

      var tables = ImmutableArray.CreateBuilder<ResolvedTable>(models.Length);

      foreach (var model in models)
      {
         var relations = candidatesByTable[model.TableClassFullName];
         var resolved = ImmutableArray.CreateBuilder<ResolvedRelation>(relations.Count);

         foreach (var candidate in relations)
            resolved.Add(candidate.ToResolvedRelation());

         tables.Add(new ResolvedTable(model, resolved.ToImmutable()));
      }

      return new ResolveResult(tables.ToImmutable(), diagnostics.ToImmutable());
   }

   /// <summary>
   ///    Pairs the columns of a relation declared as a <c>RelationDefinition&lt;,&gt;</c> class and checks every claim
   ///    those pairs make. Each pair already names its own declaring-side and target-side property, so pairing is just
   ///    looking each name up on its own table's mapped columns — there is no foreign-key/primary-key side to work out
   ///    from cardinality, unlike the old attribute-argument form's positional matching, which this mechanism replaces
   ///    entirely. The Relation condition is left for the second pass; see <see cref="Resolve" />.
   /// </summary>
   private static bool TryPairColumns(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      ImmutableDictionary<string, TableDefinitionModel> byFullName,
      ImmutableArray<Diagnostic>.Builder diagnostics,
      out RelationCandidate candidate
   )
   {
      candidate = null!;

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

      var pairs = relation.KeyPairs;
      var joinedKeys = ImmutableArray.CreateBuilder<JoinedKeyPair>(pairs.Length);
      var beforeResolving = diagnostics.Count;

      foreach (var pair in pairs)
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
         return false;

      var resolvedJoinedKeys = joinedKeys.ToImmutable();

      if (!CheckKeyPairClaims(model, relation, target, resolvedJoinedKeys, diagnostics))
         return false;

      candidate = new RelationCandidate(model, relation, target, resolvedJoinedKeys);

      return true;
   }

   /// <summary>
   ///    Checks a relation's resolved key pairs against the target's uniqueness claim and both tables' tenancy
   ///    claims. Returns <see langword="false" />, dropping the relation, only when a pair reaches a nullable
   ///    <c>[Unique]</c> target column (<c>PGSQL0035</c>) — the rest are warnings that report and keep the relation.
   /// </summary>
   private static bool CheckKeyPairClaims(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      TableDefinitionModel target,
      ImmutableArray<JoinedKeyPair> joinedKeys,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      var beforeChecking = diagnostics.Count;

      foreach (var pair in joinedKeys)
      {
         if (!pair.TargetKey.IsUnique || !pair.TargetKey.IsNullable)
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationKeyPairsAgainstNullableUniqueColumn,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               target.TableClassName,
               pair.TargetKey.PropertyName
            )
         );
      }

      if (diagnostics.Count != beforeChecking)
         return false;

      // Reaching one row is a claim, exactly like every other claim a Table definition makes, so this is a warning
      // rather than a refusal — and it only applies to a relation to one row. A relation to many rows is allowed to
      // reach several by definition.
      if (!relation.IsToMany && !PairedColumnsClaimUniqueness(target, joinedKeys))
      {
         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationToOneRowMayReachSeveral,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               target.TableClassName
            )
         );
      }

      CheckTenancyPairing(model, relation, target, joinedKeys, diagnostics);

      return true;
   }

   /// <summary>
   ///    Whether the target-side columns of <paramref name="joinedKeys" /> contain something the target claims
   ///    unique — its whole primary key, or any single <c>[Unique]</c> column. A superset of a unique set is still
   ///    unique, which is why this checks containment rather than equality.
   /// </summary>
   private static bool PairedColumnsClaimUniqueness(TableDefinitionModel target, ImmutableArray<JoinedKeyPair> joinedKeys)
   {
      var pairedTargetColumns = new HashSet<PropertyDefinitionModel>(joinedKeys.Select(x => x.TargetKey));

      if (!target.PrimaryKeys.IsEmpty && target.PrimaryKeys.All(pairedTargetColumns.Contains))
         return true;

      return pairedTargetColumns.Any(x => x.IsUnique);
   }

   /// <summary>
   ///    Drops every relation whose <c>Condition</c> touches a member that will not exist on the generated data type
   ///    it is read against — a member that is neither a mapped column nor a relation that resolved — because the lift
   ///    into generated source would otherwise fail with no line in the developer's own code to fix.
   /// </summary>
   /// <remarks>
   ///    Run to a fixed point, because dropping one relation shrinks what a condition on another may touch: a
   ///    condition reaching through a relation that this pass itself drops has to be dropped in turn. Each round only
   ///    removes relations, so the member sets shrink monotonically and the loop settles after at most one round per
   ///    relation. A dropped relation reports once and is never re-checked, so no round can report it twice.
   /// </remarks>
   private static void DropRelationsWhoseConditionCannotBeCarried(
      ImmutableArray<TableDefinitionModel> models,
      Dictionary<string, List<RelationCandidate>> candidatesByTable,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      bool droppedAny;

      do
      {
         droppedAny = false;

         var memberNamesByTable = models.ToDictionary(
            x => x.TableClassFullName,
            x => MemberNames(x, candidatesByTable[x.TableClassFullName]),
            StringComparer.Ordinal
         );

         foreach (var candidates in candidatesByTable.Values)
         {
            // Reverse, so removing an entry cannot skip the one after it.
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

   /// <summary>
   ///    Whether every member the candidate's condition touches directly on either parameter exists on that table's
   ///    generated data type. Reports every offending member rather than only the first, because each is a separate
   ///    mistake.
   /// </summary>
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

   /// <summary>
   ///    Every member a table's generated data type mirrors: a mapped column, or a relation that resolved. Read from
   ///    the relations still standing rather than from the ones declared, which is what makes the check agree with
   ///    what <see cref="TableRelationsSourceBuilder" /> actually emits.
   /// </summary>
   private static HashSet<string> MemberNames(TableDefinitionModel model, List<RelationCandidate> candidates)
   {
      var names = new HashSet<string>(StringComparer.Ordinal);

      foreach (var property in model.DataProperties)
         names.Add(property.PropertyName);

      foreach (var candidate in candidates)
         names.Add(candidate.Relation.PropertyName);

      return names;
   }

   /// <summary>
   ///    Warns on every tenancy column of either table that the joined key pairs do not pin to a tenancy column on the
   ///    other table — pair-based and direction-free, so it reads the same regardless of which side declared the
   ///    relation or which side happens to hold the foreign key. A tenancy column outside every pair warns too: the
   ///    join never touches it at all, which pins the tenant even less than pairing it against the wrong property
   ///    would. Reports once per unpinned tenancy column on either table, so a relation missing both can report twice.
   /// </summary>
   /// <remarks>
   ///    The two halves of the rule are not equally strict, because they are not equally actionable. A tenancy column
   ///    that is paired against something which is not a tenancy column always warns: that is the shape the check
   ///    exists for, and the developer can always answer it, either by marking the other column or by pairing a
   ///    different one. A tenancy column that is in no pair at all warns only when the other table is tenanted too.
   ///    Where it is not, the join cannot reach another tenant's rows — the far side's rows belong to no tenant — and
   ///    there is no column over there to pair with even if one wanted to, so the warning would name a problem with no
   ///    fix. A tenanted table reading a shared, untenanted lookup is the common shape that falls under this, and the
   ///    same reasoning applies facing the other way.
   /// </remarks>
   private static void CheckTenancyPairing(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      TableDefinitionModel target,
      ImmutableArray<JoinedKeyPair> joinedKeys,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      CheckTenancySide(model, relation, model, target, joinedKeys, isDeclaringSide: true, diagnostics);
      CheckTenancySide(model, relation, target, model, joinedKeys, isDeclaringSide: false, diagnostics);
   }

   /// <summary>
   ///    Checks every tenancy column of <paramref name="owner" /> — the declaring table when <paramref name="isDeclaringSide" />
   ///    is <see langword="true" />, the target otherwise — against the pair that names it, if any.
   /// </summary>
   private static void CheckTenancySide(
      TableDefinitionModel model,
      RelationDeclarationModel relation,
      TableDefinitionModel owner,
      TableDefinitionModel otherSide,
      ImmutableArray<JoinedKeyPair> joinedKeys,
      bool isDeclaringSide,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      foreach (var tenancyColumn in owner.TenancyColumns)
      {
         var pair = joinedKeys.FirstOrDefault(x => ReferenceEquals(isDeclaringSide ? x.ThisKey : x.TargetKey, tenancyColumn));
         var counterpart = pair is null ? null : isDeclaringSide ? pair.TargetKey : pair.ThisKey;

         if (counterpart is { IsTenancy: true })
            continue;

         // Unpinned rather than mispaired, against a table that carries no tenant of its own: nothing to reach across
         // and nothing to pair with. See the remarks on CheckTenancyPairing.
         if (counterpart is null && otherSide.TenancyColumns.IsEmpty)
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationCouldReachAcrossTenants,
               relation.Location,
               model.TableClassName,
               relation.PropertyName,
               tenancyColumn.PropertyName
            )
         );
      }
   }

   /// <summary>
   ///    Warns, per <c>PGSQL0034</c>, when one table declares two relations pairing the same key columns where one
   ///    carries a Relation condition and another does not — the unconditioned one silently returns every kind the
   ///    conditioned ones distinguish between.
   /// </summary>
   /// <remarks>
   ///    Relations are grouped by the columns they read on the declaring side, deliberately not by the target or by
   ///    the target-side columns. The shape this warning exists for is the polymorphic one, where relations sharing a
   ///    declaring-side column reach <em>different</em> targets — and so necessarily name different columns over
   ///    there, since those are different tables. Grouping on anything from the target side would put each relation in
   ///    a group of its own and the warning would never fire on the case that motivates it.
   /// </remarks>
   private static void CheckForForgottenConditions(
      TableDefinitionModel model,
      ImmutableDictionary<string, TableDefinitionModel> byFullName,
      ImmutableArray<Diagnostic>.Builder diagnostics
   )
   {
      var byShape = new Dictionary<string, List<RelationDeclarationModel>>(StringComparer.Ordinal);

      foreach (var relation in model.Relations)
      {
         if (!byFullName.ContainsKey(relation.TargetClassFullName))
            continue;

         var pairs = relation.KeyPairs;

         if (pairs.Any(x => x.DeclaringPropertyName is null || x.TargetPropertyName is null))
            continue;

         var shapeKey = string.Join("|", pairs.Select(x => x.DeclaringPropertyName).OrderBy(x => x, StringComparer.Ordinal));

         if (!byShape.TryGetValue(shapeKey, out var group))
         {
            group = [];
            byShape[shapeKey] = group;
         }

         group.Add(relation);
      }

      foreach (var group in byShape.Values)
      {
         if (group.Count < 2 || group.All(x => x.Condition is null))
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

   /// <summary>The globally qualified name of a generated type, which is how generated source always names one.</summary>
   internal static string QualifyTypeName(string namespaceName, string typeName)
   {
      return string.IsNullOrWhiteSpace(namespaceName) ? $"global::{typeName}" : $"global::{namespaceName}.{typeName}";
   }
}
