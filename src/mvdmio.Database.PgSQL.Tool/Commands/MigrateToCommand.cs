using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Tool.Building;
using mvdmio.Database.PgSQL.Tool.Configuration;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db migrate to &lt;identifier&gt;
/// </summary>
internal static class MigrateToCommand
{
   public static Command Create()
   {
      var identifierArgument = new Argument<long>("identifier")
      {
         Description = "The migration identifier to migrate up to (inclusive), e.g. 202602161430"
      };

      var connectionStringOption = new Option<string?>("--connection-string")
      {
         Description = "Override the connection string from the configuration file"
      };

      var environmentOption = new Option<string?>("--environment", "-e")
      {
         Description = "The environment to use (looks up the connection string from connectionStrings in .mvdmio-migrations.yml)"
      };

      var command = new Command("to", "Migrate the database up to a specific version");
      command.Arguments.Add(identifierArgument);
      command.Options.Add(connectionStringOption);
      command.Options.Add(environmentOption);

      command.SetAction(async (parseResult, cancellationToken) =>
      {
         var targetIdentifier = parseResult.GetValue(identifierArgument);
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
         var allMigrationsUpToTarget = migrationRetriever.RetrieveMigrations()
            .Where(m => m.Identifier <= targetIdentifier)
            .OrderBy(x => x.Identifier)
            .ToArray();

         if (allMigrationsUpToTarget.Length == 0)
         {
            Console.Error.WriteLine($"Error: No migrations found with identifier <= {targetIdentifier}.");
            return;
         }

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

            if (migrationInfo is not null && migrationInfo.Value.Identifier <= targetIdentifier)
            {
               Console.WriteLine($"Empty database detected. Will apply schema file: {Path.GetFileName(schemaFilePath)}");
               Console.WriteLine($"Schema file contains migration version: {migrationInfo.Value.Identifier} ({migrationInfo.Value.Name})");
               Console.WriteLine();
            }
            else if (migrationInfo is not null)
            {
               Console.WriteLine($"Schema file version ({migrationInfo.Value.Identifier}) is newer than target ({targetIdentifier}). Running migrations instead.");
               Console.WriteLine();
            }
         }
         else
         {
            var pendingCount = allMigrationsUpToTarget.Count(m => alreadyExecuted.All(e => e.Identifier != m.Identifier));
            Console.WriteLine($"Found {allMigrationsUpToTarget.Length} migration(s) up to {targetIdentifier}, {alreadyExecuted.Length} already applied.");

            if (pendingCount == 0)
            {
               Console.WriteLine("Database is already up to date for the specified target.");
               return;
            }

            Console.WriteLine();
         }

         // The migrator handles schema-first logic internally
         await migrator.MigrateDatabaseToAsync(targetIdentifier, cancellationToken);

         var finalExecuted = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
         var appliedCount = finalExecuted.Length - alreadyExecuted.Length;

         Console.WriteLine($"Migration complete. {appliedCount} migration(s) applied.");
      });

      return command;
   }
}
