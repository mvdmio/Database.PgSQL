using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

[Obsolete("Temporary backfill for upgrading scope-less migration tables. Removed in the next major version, when the scope column becomes NOT NULL.")]
internal static class ScopeBackfillMatcher
{
   public static ScopeBackfillResult Match(
      IReadOnlyCollection<ExecutedMigrationModel> executedMigrations,
      IEnumerable<IDbMigration> discoveredMigrations)
   {
      var scopesByIdentifier = discoveredMigrations
         .GroupBy(m => m.Identifier)
         .ToDictionary(g => g.Key, g => g.Select(m => m.Scope).Distinct(StringComparer.Ordinal).ToArray());

      var assignments = new List<ScopeAssignment>();
      var unattributed = new List<ExecutedMigrationModel>();

      foreach (var row in executedMigrations.Where(x => x.Scope is null))
      {
         if (scopesByIdentifier.TryGetValue(row.Identifier, out var scopes) && scopes.Length == 1)
            assignments.Add(new ScopeAssignment(row.Identifier, scopes[0]));
         else
            unattributed.Add(row);
      }

      return new ScopeBackfillResult(assignments, unattributed);
   }
}

internal readonly record struct ScopeAssignment(long Identifier, string Scope);

internal sealed record ScopeBackfillResult(IReadOnlyList<ScopeAssignment> Assignments, IReadOnlyList<ExecutedMigrationModel> Unattributed);
