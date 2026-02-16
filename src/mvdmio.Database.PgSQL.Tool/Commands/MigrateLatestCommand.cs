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

      var command = new Command("latest", "Migrate the database to the latest version");
      command.Options.Add(connectionStringOption);

      command.SetAction(async (parseResult, cancellationToken) =>
      {
         var connectionStringOverride = parseResult.GetValue(connectionStringOption);

         var config = ToolConfiguration.Load();
         var connectionString = connectionStringOverride ?? config.ConnectionString;

         if (string.IsNullOrWhiteSpace(connectionString))
         {
            Console.Error.WriteLine("Error: No connection string provided.");
            Console.Error.WriteLine("Specify one in .mvdmio-migrations.yml or use --connection-string.");
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
