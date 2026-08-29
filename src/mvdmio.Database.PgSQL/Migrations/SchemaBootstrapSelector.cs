using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Pure decision logic for per-scope schema-first bootstrap: which embedded schemas to apply, and which
///    baseline rows those applications may record. A schema applies when at least one scope its assembly
///    vouches for has no watermark yet, unless a migrate-to target is older than a header identifier in that
///    file. Baseline rows are recorded only for vouched scopes that were empty before the run; legacy
///    scope-less header lines are recorded only on a globally empty database.
/// </summary>
internal static class SchemaBootstrapSelector
{
   /// <summary>
   ///    Returns whether an assembly's embedded schema should be applied for this run.
   /// </summary>
   /// <param name="vouchedScopes">Scopes the schema file's assembly vouches for.</param>
   /// <param name="scopesWithWatermark">Scopes that already have at least one executed migration row.</param>
   /// <param name="headerLines">Migration-version lines parsed from the schema file header.</param>
   /// <param name="targetIdentifier">
   ///    Optional migrate-to ceiling. When set, a schema whose header contains any identifier above the
   ///    target is skipped; other assemblies are still considered independently.
   /// </param>
   public static bool ShouldApplySchema(
      IReadOnlyCollection<string> vouchedScopes,
      IReadOnlySet<string> scopesWithWatermark,
      IReadOnlyList<SchemaFileMigrationInfo> headerLines,
      long? targetIdentifier)
   {
      if (!vouchedScopes.Any(scope => !scopesWithWatermark.Contains(scope)))
         return false;

      if (targetIdentifier.HasValue && headerLines.Any(info => info.Identifier > targetIdentifier.Value))
         return false;

      return true;
   }

   /// <summary>
   ///    Filters <see cref="SchemaBaselineSelector"/> baselines to those this run may insert.
   /// </summary>
   /// <param name="baselines">Baselines selected from the headers of schemas that were applied.</param>
   /// <param name="scopesWithWatermarkBeforeRun">Scopes that already had a watermark before schema apply.</param>
   /// <param name="databaseWasGloballyEmpty">
   ///    True when the migrations table was missing or had no rows at the start of the run. Legacy
   ///    scope-less header lines are recorded only in that case.
   /// </param>
   public static IReadOnlyList<SchemaFileMigrationInfo> FilterBaselinesToRecord(
      IReadOnlyList<SchemaFileMigrationInfo> baselines,
      IReadOnlySet<string> scopesWithWatermarkBeforeRun,
      bool databaseWasGloballyEmpty)
   {
      return baselines
         .Where(baseline => baseline.Scope is null
            ? databaseWasGloballyEmpty
            : !scopesWithWatermarkBeforeRun.Contains(baseline.Scope))
         .ToArray();
   }
}
