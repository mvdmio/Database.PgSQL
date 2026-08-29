using Microsoft.Extensions.Logging;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Applies embedded schemas for assemblies that still have at least one empty vouched scope, in
///    constructor order. A migrate-to target skips only the schemas whose headers exceed it. Baseline rows
///    are recorded for vouched scopes that had no watermark before this run; legacy scope-less header lines
///    are recorded only when the database was globally empty.
/// </summary>
internal static class SchemaBootstrapApplier
{
   /// <summary>
   ///    Applies the schemas this run should bootstrap. Returns true when at least one schema file was applied.
   /// </summary>
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
         // Pre-create the migrations table so schema files that also try to create it
         // (with or without IF NOT EXISTS) don't conflict within the same transaction.
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
               "the assembly does not vouch for that scope, so no baseline row is recorded and that scope's migrations run from its own watermark. " +
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

   /// <summary>
   ///    Records a baseline after <see cref="MigrationsTableManager.EnsureTableAsync"/>, so the scope column exists.
   /// </summary>
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

   /// <summary>
   ///    One embedded schema selected for apply: the file contents plus the header <see cref="SchemaBaselineSelector"/>
   ///    needs to record baselines.
   /// </summary>
   private sealed record SchemaToApply(string Content, SchemaFileHeader Header);
}
