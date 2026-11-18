using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Migrations;

internal static class SchemaBootstrapSelector
{
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

   public static IReadOnlyCollection<string> GetVouchedScopes(Assembly assembly, IEnumerable<IDbMigration> discoveredMigrations)
   {
      var scopes = new HashSet<string>(StringComparer.Ordinal);

      var assemblyName = assembly.GetName().Name;
      if (assemblyName is not null)
         scopes.Add(assemblyName);

      foreach (var migration in discoveredMigrations)
      {
         if (migration.GetType().Assembly == assembly)
            scopes.Add(migration.Scope);
      }

      return scopes;
   }

   public static IReadOnlySet<string> GetScopesWithWatermark(IEnumerable<ExecutedMigrationModel> executedMigrations)
   {
      return executedMigrations
         .Where(row => row.Scope is not null)
         .Select(row => row.Scope!)
         .ToHashSet(StringComparer.Ordinal);
   }
}
