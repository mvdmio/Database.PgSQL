using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Pure per-scope selection: decides which discovered migrations are pending, by the rule stated on
///    <see cref="IDatabaseMigrator.MigrateDatabaseToLatestAsync" />. The migrator and the db tool both select
///    through it, so they cannot disagree about what is pending.
///    Executed rows without a scope belong to no scope: they count toward no baseline, no watermark and no
///    membership check, so an un-backfilled legacy row can never silently hide or unhide a migration.
/// </summary>
internal static class PendingMigrationSelector
{
   /// <summary>
   ///    Selects the migrations to run, ordered by identifier across all scopes.
   /// </summary>
   /// <param name="executedMigrations">All rows recorded in the migrations table.</param>
   /// <param name="discoveredMigrations">All discovered migrations.</param>
   /// <param name="targetIdentifier">Optional global ceiling: every scope advances up to this identifier (inclusive).</param>
   /// <returns>The pending migrations, each with whether it is out-of-order and its scope's watermark.</returns>
   public static IReadOnlyList<PendingMigration> SelectPending(
      IReadOnlyCollection<ExecutedMigrationModel> executedMigrations,
      IEnumerable<IDbMigration> discoveredMigrations,
      long? targetIdentifier = null)
   {
      var recordedScopes = executedMigrations
         .Where(x => x.Scope is not null)
         .GroupBy(x => x.Scope!, StringComparer.Ordinal)
         .ToDictionary(
            g => g.Key,
            g => new RecordedScope(
               Baseline: g.Min(x => x.Identifier),
               Watermark: g.Max(x => x.Identifier),
               Identifiers: g.Select(x => x.Identifier).ToHashSet()),
            StringComparer.Ordinal);

      var pending = new List<PendingMigration>();

      foreach (var migration in discoveredMigrations)
      {
         if (targetIdentifier.HasValue && migration.Identifier > targetIdentifier.Value)
            continue;

         if (!recordedScopes.TryGetValue(migration.Scope, out var recordedScope))
         {
            pending.Add(new PendingMigration(migration, ScopeWatermark: null));
            continue;
         }

         if (migration.Identifier <= recordedScope.Baseline || recordedScope.Identifiers.Contains(migration.Identifier))
            continue;

         pending.Add(new PendingMigration(migration, recordedScope.Watermark));
      }

      return pending
         .OrderBy(x => x.Migration.Identifier)
         .ToArray();
   }

   /// <summary>
   ///    What the rows recorded for one scope say: its baseline, its watermark and every identifier it has a row for.
   /// </summary>
   private sealed record RecordedScope(long Baseline, long Watermark, HashSet<long> Identifiers);
}
