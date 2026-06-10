using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Pure per-scope watermark selection: decides which discovered migrations are pending.
///    A migration is pending when its identifier is ahead of the highest executed identifier within its own
///    scope (the scope's watermark) and, when a target identifier is supplied, at or below that target.
///    Executed rows without a scope are excluded from every scope's watermark, so an un-backfilled legacy row
///    can never silently suppress a migration.
/// </summary>
internal static class PendingMigrationSelector
{
   /// <summary>
   ///    Selects the migrations to run, ordered by identifier.
   /// </summary>
   /// <param name="executedMigrations">All rows recorded in the migrations table.</param>
   /// <param name="discoveredMigrations">All discovered migrations.</param>
   /// <param name="targetIdentifier">Optional global ceiling: every scope advances up to this identifier (inclusive).</param>
   public static IReadOnlyList<IDbMigration> SelectPending(
      IReadOnlyCollection<ExecutedMigrationModel> executedMigrations,
      IEnumerable<IDbMigration> discoveredMigrations,
      long? targetIdentifier = null)
   {
      var watermarks = executedMigrations
         .Where(x => x.Scope is not null)
         .GroupBy(x => x.Scope!, StringComparer.Ordinal)
         .ToDictionary(g => g.Key, g => g.Max(x => x.Identifier), StringComparer.Ordinal);

      return discoveredMigrations
         .Where(m => !targetIdentifier.HasValue || m.Identifier <= targetIdentifier.Value)
         .Where(m => !watermarks.TryGetValue(m.Scope, out var watermark) || m.Identifier > watermark)
         .OrderBy(m => m.Identifier)
         .ToArray();
   }
}
