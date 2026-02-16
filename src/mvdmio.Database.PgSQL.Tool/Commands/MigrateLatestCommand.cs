using System.CommandLine;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Tool.Building;
using mvdmio.Database.PgSQL.Tool.Configuration;

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

         var migrationRetriever = new ReflectionMigrationRetriever(assembly);
         var allMigrations = migrationRetriever.RetrieveMigrations().OrderBy(x => x.Identifier).ToArray();

         Console.WriteLine();

         await using var connection = new DatabaseConnection(connectionString);
         var migrator = new DatabaseMigrator(connection, migrationRetriever);

         var alreadyExecuted = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
         var pendingCount = allMigrations.Count(m => alreadyExecuted.All(e => e.Identifier != m.Identifier));

         Console.WriteLine($"Found {allMigrations.Length} migration(s), {alreadyExecuted.Length} already applied.");

         if (pendingCount == 0)
         {
            Console.WriteLine("Database is already up to date.");
            return;
         }

         Console.WriteLine();

         var appliedCount = 0;

         foreach (var migration in allMigrations)
         {
            if (alreadyExecuted.Any(e => e.Identifier == migration.Identifier))
               continue;

            Console.Write($"Applying migration {migration.Identifier} - {migration.Name}...");
            await migrator.RunAsync(migration, cancellationToken);
            Console.WriteLine(" done");
            appliedCount++;
         }

         Console.WriteLine();
         Console.WriteLine($"Migration complete. {appliedCount} migration(s) applied.");
      });

      return command;
   }
}
