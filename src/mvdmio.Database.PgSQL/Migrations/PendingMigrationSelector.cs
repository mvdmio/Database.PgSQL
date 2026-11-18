using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

internal static class PendingMigrationSelector
{
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

   private sealed record RecordedScope(long Baseline, long Watermark, HashSet<long> Identifiers);
}
