using mvdmio.Database.PgSQL.Connectors.Schema;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Tool.Cleanup;
using mvdmio.Database.PgSQL.Tool.Configuration;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db cleanup
/// </summary>
internal static class CleanupCommand
{
   public static Command Create()
   {
      var command = new Command("cleanup", "Pull schemas for all environments and delete obsolete migration files");

      command.SetAction(async (_, cancellationToken) =>
      {
         var config = ToolConfigurationLoader.Load();

         if (config.ConnectionStrings is null || config.ConnectionStrings.Count == 0)
         {
            Console.Error.WriteLine("Error: No environments configured.");
            Console.Error.WriteLine("Add connectionStrings to .mvdmio-migrations.yml before running cleanup.");
            return;
         }

         var schemasDirectory = ToolPathResolver.GetSchemasDirectoryPath(config);
         var migrationsDirectory = ToolPathResolver.GetMigrationsDirectoryPath(config);
         Directory.CreateDirectory(schemasDirectory);

         var environmentMigrationIdentifiers = new List<long?>();

         foreach (KeyValuePair<string, string> environment in config.ConnectionStrings)
         {
            var environmentName = environment.Key;
            var connectionString = environment.Value;
            var schemaPath = Path.Combine(schemasDirectory, $"schema.{environmentName}.sql");

            Console.WriteLine($"Pulling schema for '{environmentName}'...");

            await using var connection = new DatabaseConnection(connectionString);
            var schemaExtractor = new SchemaExtractor(connection);
            var script = await schemaExtractor.GenerateSchemaScriptAsync(cancellationToken);
            await File.WriteAllTextAsync(schemaPath, script, cancellationToken);

            var migrationInfo = SchemaFileParser.ParseMigrationVersion(script);
            environmentMigrationIdentifiers.Add(migrationInfo?.Identifier);

            if (migrationInfo is null)
               Console.WriteLine($"  Wrote {schemaPath} (no recorded migration version)");
            else
               Console.WriteLine($"  Wrote {schemaPath} (migration {migrationInfo.Value.Identifier}: {migrationInfo.Value.Name})");
         }

         Console.WriteLine();

         var plan = MigrationCleanupPlanner.Plan(migrationsDirectory, environmentMigrationIdentifiers);

         if (plan.SkipReason is not null)
         {
            Console.WriteLine($"Cleanup skipped: {plan.SkipReason}");
            Console.WriteLine("No migration files were deleted.");
            return;
         }

         Console.WriteLine($"Lowest migration version across environments: {plan.LowestMigrationIdentifier}");

         if (plan.FilesToDelete.Length == 0)
         {
            Console.WriteLine("No migration files are older than the lowest environment version.");
            return;
         }

         foreach (var file in plan.FilesToDelete)
         {
            File.Delete(file);
            Console.WriteLine($"Deleted {file}");
         }

         Console.WriteLine();
         Console.WriteLine($"Cleanup complete. Deleted {plan.FilesToDelete.Length} migration file(s).");
      });

      return command;
   }
}
