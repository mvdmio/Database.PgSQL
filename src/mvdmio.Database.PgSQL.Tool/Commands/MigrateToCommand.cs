using System.CommandLine;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Tool.Building;
using mvdmio.Database.PgSQL.Tool.Configuration;

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

      var command = new Command("to", "Migrate the database up to a specific version");
      command.Arguments.Add(identifierArgument);
      command.Options.Add(connectionStringOption);

      command.SetAction(async (parseResult, cancellationToken) =>
      {
         var targetIdentifier = parseResult.GetValue(identifierArgument);
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
         var allMigrations = migrationRetriever.RetrieveMigrations()
            .Where(m => m.Identifier <= targetIdentifier)
            .OrderBy(x => x.Identifier)
            .ToArray();

         if (allMigrations.Length == 0)
         {
            Console.Error.WriteLine($"Error: No migrations found with identifier <= {targetIdentifier}.");
            return;
         }

         Console.WriteLine();

         await using var connection = new DatabaseConnection(connectionString);
         var migrator = new DatabaseMigrator(connection, migrationRetriever);

         var alreadyExecuted = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
         var pendingCount = allMigrations.Count(m => alreadyExecuted.All(e => e.Identifier != m.Identifier));

         Console.WriteLine($"Found {allMigrations.Length} migration(s) up to {targetIdentifier}, {alreadyExecuted.Length} already applied.");

         if (pendingCount == 0)
         {
            Console.WriteLine("Database is already up to date for the specified target.");
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
