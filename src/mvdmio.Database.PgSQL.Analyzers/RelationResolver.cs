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
      // Each table is held together with the relations of its own still standing, rather than in a table-keyed lookup
      // beside the models, so the two cannot come to disagree about which relations belong to which table.
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

   /// <summary>
   ///    Pairs the columns of a relation declared as a <c>RelationDefinition&lt;,&gt;</c> class and checks every claim
   ///    those pairs make. Each pair already names its own declaring-side and target-side property, so pairing is just
   ///    looking each name up on its own table's mapped columns — there is no foreign-key/primary-key side to work out
   ///    from cardinality, unlike the old attribute-argument form's positional matching, which this mechanism replaces
   ///    entirely. The Relation condition is left for the second pass; see <see cref="Resolve" />.
   /// </summary>
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

   /// <summary>
   ///    Checks a relation's resolved key pairs against the target's uniqueness claim and both tables' tenancy
   ///    claims. Returns <see langword="false" />, dropping the relation, only when a pair reaches a nullable
   ///    <c>[Unique]</c> target column (<c>PGSQL0035</c>) — the rest are warnings that report and keep the relation.
   /// </summary>
   private static bool CheckKeyPairClaims(RelationCandidate candidate, ImmutableArray<Diagnostic>.Builder diagnostics)
   {
      var relation = candidate.Relation;
      var beforeChecking = diagnostics.Count;

      foreach (var pair in candidate.JoinedKeys)
      {
         if (!pair.TargetKey.IsUnique || !pair.TargetKey.IsNullable)
            continue;

         diagnostics.Add(
            Diagnostic.Create(
               TableRepositoryDiagnostics.RelationKeyPairsAgainstNullableUniqueColumn,
               relation.Location,
               candidate.Model.TableClassName,
               relation.PropertyName,
               candidate.Target.TableClassName,
               pair.TargetKey.PropertyName
            )
         );
      }

      if (diagnostics.Count != beforeChecking)
         return false;

      // Reaching one row is a claim, exactly like every other claim a Table definition makes, so this is a warning
      // rather than a refusal — and it only applies to a relation to one row. A relation to many rows is allowed to
      // reach several by definition.
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
   private static void CheckTenancyPairing(RelationCandidate candidate, ImmutableArray<Diagnostic>.Builder diagnostics)
   {
      CheckTenancySide(candidate, candidate.Model, candidate.Target, isDeclaringSide: true, diagnostics);
      CheckTenancySide(candidate, candidate.Target, candidate.Model, isDeclaringSide: false, diagnostics);
   }

   /// <summary>
   ///    Checks every tenancy column of <paramref name="owner" /> — the declaring table when <paramref name="isDeclaringSide" />
   ///    is <see langword="true" />, the target otherwise — against the pair that names it, if any.
   /// </summary>
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

         // Unpinned rather than mispaired, against a table that carries no tenant of its own: nothing to reach across
         // and nothing to pair with. See the remarks on CheckTenancyPairing.
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
      // Every pair of a relation that reaches here names both of its sides: one that does not is reported as
      // PGSQL0030 and dropped by RelationDeclarationParser, so it never becomes a RelationDeclarationModel.
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

   /// <summary>The globally qualified name of a generated type, which is how generated source always names one.</summary>
   internal static string QualifyTypeName(string namespaceName, string typeName)
   {
      return string.IsNullOrWhiteSpace(namespaceName) ? $"global::{typeName}" : $"global::{namespaceName}.{typeName}";
   }
}
