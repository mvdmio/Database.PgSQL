using mvdmio.Database.PgSQL.Connectors.Schema;
using mvdmio.Database.PgSQL.Tool.Configuration;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db pull
/// </summary>
internal static class PullCommand
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

      var command = new Command("pull", "Pull the database schema and save it as a schema.<env>.sql file");
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

         var schemasDir = config.GetSchemasDirectoryPath();
         Directory.CreateDirectory(schemasDir);

         var environmentName = config.ResolveEnvironmentName(connectionStringOverride, environmentOverride);
         var fileName = environmentName is not null ? $"schema.{environmentName}.sql" : "schema.sql";
         var outputPath = Path.Combine(schemasDir, fileName);

         Console.WriteLine("Connecting to database...");

         await using var connection = new DatabaseConnection(connectionString);

         Console.WriteLine("Extracting schema...");

         // Use a SchemaExtractor with the configured migration table settings
         // so the migration schema is excluded from the output
         var migrationTableConfig = config.GetMigrationTableConfiguration();
         var schemaExtractor = new SchemaExtractor(connection, migrationTableConfig);
         var script = await schemaExtractor.GenerateSchemaScriptAsync(cancellationToken);

         await File.WriteAllTextAsync(outputPath, script, cancellationToken);

         Console.WriteLine();
         Console.WriteLine($"Schema written to {outputPath}");
      });

      return command;
   }
}
