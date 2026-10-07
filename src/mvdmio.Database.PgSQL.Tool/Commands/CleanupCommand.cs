using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Models;
using mvdmio.Database.PgSQL.Tool.Cleanup;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Migrations;
using mvdmio.Database.PgSQL.Tool.Pull;
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
         var schemaExportService = new SchemaExportService();

         if (config.ConnectionStrings is null || config.ConnectionStrings.Count == 0)
         {
            Console.Error.WriteLine("Error: No environments configured.");
            Console.Error.WriteLine("Add connectionStrings to .mvdmio-migrations.yml before running cleanup.");
            return;
         }

         var schemasDirectory = ToolPathResolver.GetSchemasDirectoryPath(config);
         var migrationsDirectory = ToolPathResolver.GetMigrationsDirectoryPath(config);
         Directory.CreateDirectory(schemasDirectory);

         // Build the project before touching any environment, so a build failure rewrites no schema file and
         // deletes nothing.
         var project = new MigrationProjectLoader().Load(ToolPathResolver.GetProjectPath(config));
         var runtimeFactory = new DatabaseMigrationRuntimeFactory();
         var environments = new List<CleanupEnvironment>();

         foreach (KeyValuePair<string, string> environment in config.ConnectionStrings)
         {
            var environmentName = environment.Key;
            var connectionString = environment.Value;
            var schemaPath = Path.Combine(schemasDirectory, $"schema.{environmentName}.sql");

            Console.WriteLine($"Pulling schema for '{environmentName}'...");

            SchemaExportResult schemaResult;

            try
            {
               schemaResult = await schemaExportService.ExportAsync(connectionString, config.Schemas, config.Scopes, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
               Console.Error.WriteLine($"Error: {ex.Message}");
               return;
            }

            var script = schemaResult.Script;
            await File.WriteAllTextAsync(schemaPath, script, cancellationToken);

            foreach (var warning in schemaResult.Warnings)
               Console.WriteLine($"  Warning: {warning}");

            // A schema may carry one version line per scope; the lowest one is the conservative bound
            // for deleting migration files (anything below it is baked into every scope's baseline).
            var migrationInfos = SchemaFileParser.ParseMigrationVersion(script);
            var lowestMigrationInfo = migrationInfos.Count == 0
               ? (SchemaFileMigrationInfo?)null
               : migrationInfos.MinBy(x => x.Identifier);

            // Rows are read as recorded, not backfilled: a row without a scope counts toward nothing, which can
            // only keep more files.
            await using var runtime = runtimeFactory.Create(connectionString, environmentName, project);
            var executedMigrations = await runtime.RetrieveRecordedMigrationsAsync(cancellationToken);
            environments.Add(new CleanupEnvironment(environmentName, lowestMigrationInfo?.Identifier, executedMigrations));

            if (lowestMigrationInfo is null)
               Console.WriteLine($"  Wrote {schemaPath} (no recorded migration version)");
            else
               Console.WriteLine($"  Wrote {schemaPath} (migration {lowestMigrationInfo.Value.Identifier}: {lowestMigrationInfo.Value.Name})");
         }

         Console.WriteLine();

         var plan = MigrationCleanupPlanner.Plan(migrationsDirectory, project.Migrations, environments);

         if (plan.SkipReason is not null)
         {
            Console.WriteLine($"Cleanup skipped: {plan.SkipReason}");
            Console.WriteLine("No migration files were deleted.");
            return;
         }

         Console.WriteLine($"Lowest migration version across environments: {plan.LowestMigrationIdentifier}");

         foreach (var pendingFile in plan.PendingFilesKept)
            Console.WriteLine($"Kept {pendingFile.Path}: still pending in {string.Join(", ", pendingFile.PendingEnvironments)}");

         if (plan.FilesToDelete.Length == 0)
         {
            Console.WriteLine(plan.PendingFilesKept.Length == 0
               ? "No migration files are older than the lowest environment version."
               : "No migration files were deleted.");
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
