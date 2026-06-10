using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Temporary upgrade aid: matches executed rows without a scope to discovered migrations by identifier so
///    legacy rows can be attributed to their scope. Only rows whose scope is still null are considered (an
///    already-scoped row is never overwritten), so concurrent runners can each safely fill the rows they
///    recognize. Rows that cannot be attributed are reported so the caller can warn about them.
/// </summary>
[Obsolete("Temporary backfill for upgrading scope-less migration tables. Removed in the next major version, when the scope column becomes NOT NULL.")]
internal static class ScopeBackfillMatcher
{
   /// <summary>
   ///    Matches scope-less executed rows to discovered migrations by identifier. A row is attributed only
   ///    when every discovered migration with that identifier agrees on a single scope; ambiguous or
   ///    unrecognized rows are left unattributed.
   /// </summary>
   /// <param name="executedMigrations">All rows recorded in the migrations table.</param>
   /// <param name="discoveredMigrations">All discovered migrations.</param>
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

/// <summary>
///    A scope to assign to the executed row with the given identifier (and a still-null scope).
/// </summary>
/// <param name="Identifier">Identifier of the row to attribute.</param>
/// <param name="Scope">The scope to assign.</param>
internal readonly record struct ScopeAssignment(long Identifier, string Scope);

/// <summary>
///    Result of matching scope-less executed rows against discovered migrations.
/// </summary>
/// <param name="Assignments">Rows that could be attributed, with the scope to assign.</param>
/// <param name="Unattributed">Rows no discovered migration unambiguously claims; these stay scope-less.</param>
internal sealed record ScopeBackfillResult(IReadOnlyList<ScopeAssignment> Assignments, IReadOnlyList<ExecutedMigrationModel> Unattributed);
