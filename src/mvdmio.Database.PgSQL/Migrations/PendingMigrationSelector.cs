using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

internal static class PendingMigrationSelector
{
   public static IReadOnlyList<PendingMigration> SelectPending(
      IReadOnlyCollection<ExecutedMigrationModel> executedMigrations,
      IEnumerable<IDbMigration> discoveredMigrations,
      DeployPhase phase,
      long? targetIdentifier = null)
   {
      var pending = new List<PendingMigration>();

      foreach (var status in Evaluate(executedMigrations, discoveredMigrations, targetIdentifier))
      {
         if (phase.HasFlag(DeployPhase.PreDeploy) && status.PreDeployPending)
            pending.Add(new PendingMigration(status.Migration, status.ScopeWatermark, DeployPhase.PreDeploy));

         var preDeployPartRunsFirst = !status.PreDeployPending || phase.HasFlag(DeployPhase.PreDeploy);

         if (phase.HasFlag(DeployPhase.PostDeploy) && status.PostDeployOutstanding && preDeployPartRunsFirst)
            pending.Add(new PendingMigration(status.Migration, status.ScopeWatermark, DeployPhase.PostDeploy));
      }

      return pending
         .OrderBy(x => x.Migration.Identifier)
         .ThenBy(x => x.Phase)
         .ToArray();
   }

   public static IReadOnlyList<IDbMigration> SelectWaitingPostDeployParts(
      IReadOnlyCollection<ExecutedMigrationModel> executedMigrations,
      IEnumerable<IDbMigration> discoveredMigrations,
      long? targetIdentifier = null)
   {
      return Evaluate(executedMigrations, discoveredMigrations, targetIdentifier)
         .Where(status => status.PostDeployOutstanding && !status.PreDeployPending)
         .Select(status => status.Migration)
         .OrderBy(migration => migration.Identifier)
         .ToArray();
   }

   public static IReadOnlyList<IDbMigration> SelectBlockedPostDeployParts(
      IReadOnlyCollection<ExecutedMigrationModel> executedMigrations,
      IEnumerable<IDbMigration> discoveredMigrations,
      long? targetIdentifier = null)
   {
      return Evaluate(executedMigrations, discoveredMigrations, targetIdentifier)
         .Where(status => status.PostDeployOutstanding && status.PreDeployPending)
         .Select(status => status.Migration)
         .OrderBy(migration => migration.Identifier)
         .ToArray();
   }

   private static IEnumerable<MigrationStatus> Evaluate(
      IReadOnlyCollection<ExecutedMigrationModel> executedMigrations,
      IEnumerable<IDbMigration> discoveredMigrations,
      long? targetIdentifier)
   {
      var recordedScopes = executedMigrations
         .Where(x => x.Scope is not null)
         .GroupBy(x => x.Scope!, StringComparer.Ordinal)
         .Where(g => g.Any(IsPreDeployRow))
         .ToDictionary(
            g => g.Key,
            g => CreateRecordedScope(g.ToArray()),
            StringComparer.Ordinal);

      foreach (var migration in discoveredMigrations)
      {
         if (targetIdentifier.HasValue && migration.Identifier > targetIdentifier.Value)
            continue;

         var hasPostDeployPart = PostDeployPart.Exists(migration);

         if (!recordedScopes.TryGetValue(migration.Scope, out var recordedScope))
         {
            yield return new MigrationStatus(migration, ScopeWatermark: null, PreDeployPending: true, PostDeployOutstanding: hasPostDeployPart);
            continue;
         }

         if (migration.Identifier < recordedScope.Baseline || (migration.Identifier == recordedScope.Baseline && !recordedScope.BaselineRanItself))
            continue;

         yield return new MigrationStatus(
            migration,
            recordedScope.Watermark,
            PreDeployPending: !recordedScope.PreDeployIdentifiers.Contains(migration.Identifier),
            PostDeployOutstanding: hasPostDeployPart && !recordedScope.PostDeployIdentifiers.Contains(migration.Identifier));
      }
   }

   public static bool IsPreDeployRow(ExecutedMigrationModel row)
   {
      return row.Phase != DeployPhase.PostDeploy;
   }

   private static RecordedScope CreateRecordedScope(IReadOnlyCollection<ExecutedMigrationModel> rows)
   {
      var preDeployRows = rows.Where(IsPreDeployRow).ToArray();
      var baseline = preDeployRows.Min(x => x.Identifier);

      return new RecordedScope(
         Baseline: baseline,
         BaselineRanItself: preDeployRows.Any(x => x.Identifier == baseline && x.HasRecordedPhase),
         Watermark: preDeployRows.Max(x => x.Identifier),
         PreDeployIdentifiers: preDeployRows.Select(x => x.Identifier).ToHashSet(),
         PostDeployIdentifiers: rows.Where(x => !IsPreDeployRow(x)).Select(x => x.Identifier).ToHashSet());
   }

   private sealed record RecordedScope(long Baseline, bool BaselineRanItself, long Watermark, HashSet<long> PreDeployIdentifiers, HashSet<long> PostDeployIdentifiers);

   private sealed record MigrationStatus(IDbMigration Migration, long? ScopeWatermark, bool PreDeployPending, bool PostDeployOutstanding);
}
