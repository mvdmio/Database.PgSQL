using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Pure per-scope selection: decides which discovered migrations are pending.
///    A scope's baseline is the lowest identifier recorded for it and its watermark the highest.
///    A migration is pending when no row carries its exact (scope, identifier) pair, its scope has no rows or its
///    identifier is above the scope's baseline, and, when a target identifier is supplied, it is at or below that
///    target. Everything at or below the baseline counts as present, so migrations folded into a bootstrap schema
///    never run. A pending migration below its scope's watermark is out-of-order.
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
      var rowsByScope = executedMigrations
         .Where(x => x.Scope is not null)
         .GroupBy(x => x.Scope!, StringComparer.Ordinal)
         .ToDictionary(
            g => g.Key,
            g => new ScopeRows(
               Baseline: g.Min(x => x.Identifier),
               Watermark: g.Max(x => x.Identifier),
               Identifiers: g.Select(x => x.Identifier).ToHashSet()),
            StringComparer.Ordinal);

      var pending = new List<PendingMigration>();

      foreach (var migration in discoveredMigrations)
      {
         if (targetIdentifier.HasValue && migration.Identifier > targetIdentifier.Value)
            continue;

         if (!rowsByScope.TryGetValue(migration.Scope, out var rows))
         {
            pending.Add(new PendingMigration(migration, ScopeWatermark: null));
            continue;
         }

         if (migration.Identifier <= rows.Baseline || rows.Identifiers.Contains(migration.Identifier))
            continue;

         pending.Add(new PendingMigration(migration, rows.Watermark));
      }

      return pending
         .OrderBy(x => x.Migration.Identifier)
         .ToArray();
   }

   private sealed record ScopeRows(long Baseline, long Watermark, HashSet<long> Identifiers);
}
