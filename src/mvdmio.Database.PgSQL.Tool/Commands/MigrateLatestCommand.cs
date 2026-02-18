using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Tool.Building;
using mvdmio.Database.PgSQL.Tool.Configuration;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db migrate latest
/// </summary>
internal static class MigrateLatestCommand
{
   public static Command Create()
   {
      var connectionStringOption = new Option<string?>("--connection-string")
      {
         Description = "Override the connection string from the configuration file"
      };

      var environmentOption = new Option<string?>("--environment", "-e")
      {
         Description = "The environment to use (looks up the connection string from connectionStrings in .mvdmio-migrations.yml)"
      };

      var command = new Command("latest", "Migrate the database to the latest version");
      command.Options.Add(connectionStringOption);
      command.Options.Add(environmentOption);

      command.SetAction(async (parseResult, cancellationToken) =>
      {
         var connectionStringOverride = parseResult.GetValue(connectionStringOption);
         var environmentOverride = parseResult.GetValue(environmentOption);

         var config = ToolConfiguration.Load();
         var connectionString = config.ResolveConnectionString(connectionStringOverride, environmentOverride);

         if (string.IsNullOrWhiteSpace(connectionString))
         {
            if (environmentOverride is not null)
            {
               var available = config.GetAvailableEnvironments();
               Console.Error.WriteLine($"Error: Environment '{environmentOverride}' not found in .mvdmio-migrations.yml.");

               if (available.Length > 0)
                  Console.Error.WriteLine($"Available environments: {string.Join(", ", available)}");
            }
            else
            {
               Console.Error.WriteLine("Error: No connection string provided.");
               Console.Error.WriteLine("Specify one via --connection-string, --environment, or add an entry to connectionStrings in .mvdmio-migrations.yml.");
            }

            return;
         }

         var projectPath = config.GetProjectPath();
         var assembly = ProjectBuilder.BuildAndLoadAssembly(projectPath);
         var migrationTableConfig = config.GetMigrationTableConfiguration();
         var schemaFilePath = config.GetSchemaFilePath(connectionStringOverride, environmentOverride);

         var migrationRetriever = new ReflectionMigrationRetriever(assembly);
         var allMigrations = migrationRetriever.RetrieveMigrations().OrderBy(x => x.Identifier).ToArray();

         Console.WriteLine();

         await using var connection = new DatabaseConnection(connectionString);
         var migrator = new DatabaseMigrator(connection, migrationTableConfig, schemaFilePath, migrationRetriever);

         // Check status before migrating
         var isDatabaseEmpty = await migrator.IsDatabaseEmptyAsync(cancellationToken);
         var alreadyExecuted = isDatabaseEmpty
            ? []
            : (await migrator.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();

         if (isDatabaseEmpty && schemaFilePath is not null)
         {
            var migrationInfo = await SchemaFileParser.ParseMigrationVersionFromFileAsync(schemaFilePath, cancellationToken);
            Console.WriteLine($"Empty database detected. Will apply schema file: {Path.GetFileName(schemaFilePath)}");

            if (migrationInfo is not null)
               Console.WriteLine($"Schema file contains migration version: {migrationInfo.Value.Identifier} ({migrationInfo.Value.Name})");

            Console.WriteLine();
         }
         else
         {
            var pendingCount = allMigrations.Count(m => alreadyExecuted.All(e => e.Identifier != m.Identifier));
            Console.WriteLine($"Found {allMigrations.Length} migration(s), {alreadyExecuted.Length} already applied.");

            if (pendingCount == 0)
            {
               Console.WriteLine("Database is already up to date.");
               return;
            }

            Console.WriteLine();
         }

         // The migrator handles schema-first logic internally
         await migrator.MigrateDatabaseToLatestAsync(cancellationToken);

         var finalExecuted = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
         var appliedCount = finalExecuted.Length - alreadyExecuted.Length;

         Console.WriteLine($"Migration complete. {appliedCount} migration(s) applied.");
      });

      return command;
   }
}
