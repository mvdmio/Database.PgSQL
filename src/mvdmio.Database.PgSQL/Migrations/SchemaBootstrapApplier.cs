using Microsoft.Extensions.Logging;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Migrations;

internal static class SchemaBootstrapApplier
{
   public static async Task<bool> ApplyForEmptyScopesAsync(
      DatabaseConnection connection,
      ILogger logger,
      Assembly[] assemblies,
      string? environment,
      IReadOnlyCollection<IDbMigration> discoveredMigrations,
      IReadOnlySet<string> scopesWithWatermark,
      bool databaseWasGloballyEmpty,
      long? targetIdentifier,
      CancellationToken cancellationToken)
   {
      if (assemblies.Length == 0)
         return false;

      var schemas = await EmbeddedSchemaDiscovery.ReadAllSchemaContentsAsync(assemblies, environment, cancellationToken);

      if (schemas.Count == 0)
         return false;

      var schemasToApply = new List<SchemaToApply>();

      foreach (var (content, resourceName, assembly) in schemas)
      {
         if (string.IsNullOrEmpty(content))
            continue;

         var headerLines = SchemaFileParser.ParseMigrationVersion(content);
         var vouchedScopes = SchemaBootstrapSelector.GetVouchedScopes(assembly, discoveredMigrations);

         if (!SchemaBootstrapSelector.ShouldApplySchema(vouchedScopes, scopesWithWatermark, headerLines, targetIdentifier))
            continue;

         schemasToApply.Add(new SchemaToApply(
            content,
            new SchemaFileHeader(
               resourceName,
               assembly.GetName().Name ?? assembly.ToString(),
               headerLines,
               vouchedScopes
            )
         ));
      }

      if (schemasToApply.Count == 0)
         return false;

      await connection.InTransactionAsync(async () =>
      {
         await MigrationsTableManager.EnsureTableAsync(connection, cancellationToken);

         var headers = new List<SchemaFileHeader>(schemasToApply.Count);

         foreach (var schema in schemasToApply)
         {
            await connection.Dapper.ExecuteAsync(schema.Content, ct: cancellationToken);
            headers.Add(schema.Header);
         }

         var selection = SchemaBaselineSelector.SelectBaselines(headers);

         foreach (var rejection in selection.Rejected)
         {
            logger.LogWarning(
               "Ignoring migration-version header line for scope {Scope} (identifier {Identifier}) in schema resource '{ResourceName}' from assembly '{AssemblyName}': " +
               "the assembly does not vouch for that scope, so no baseline row is recorded and that scope's migrations run according to its own recorded rows. " +
               "Declare scope ownership ('scopes' in .mvdmio-migrations.yml) and re-run 'db pull' to remove foreign scopes from the header.",
               rejection.HeaderLine.Scope,
               rejection.HeaderLine.Identifier,
               rejection.ResourceName,
               rejection.AssemblyName
            );
         }

         var baselinesToRecord = SchemaBootstrapSelector.FilterBaselinesToRecord(
            selection.Baselines,
            scopesWithWatermark,
            databaseWasGloballyEmpty);

         foreach (var baseline in baselinesToRecord)
         {
            await InsertBaselineRowAsync(connection, baseline, cancellationToken);
         }
      });

      return true;
   }

   private static async Task InsertBaselineRowAsync(
      DatabaseConnection connection,
      SchemaFileMigrationInfo baseline,
      CancellationToken cancellationToken)
   {
      await connection.Dapper.ExecuteAsync(
         """
         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at, scope)
         VALUES (:identifier, :name, :executedAtUtc, :scope)
         """,
         new Dictionary<string, object?> {
            { "identifier", baseline.Identifier },
            { "name", baseline.Name },
            { "executedAtUtc", DateTime.UtcNow },
            { "scope", baseline.Scope }
         },
         ct: cancellationToken
      );
   }

   private sealed record SchemaToApply(string Content, SchemaFileHeader Header);
}
