using mvdmio.Database.PgSQL.Connectors.Schema;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Scaffolding;
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

         var config = ToolConfigurationLoader.Load();
         var connectionString = ConnectionStringResolver.ResolveConnectionString(config, connectionStringOverride, environmentOverride);

         if (string.IsNullOrWhiteSpace(connectionString))
         {
            if (environmentOverride is not null)
            {
               var available = ConnectionStringResolver.GetAvailableEnvironments(config);
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

         var schemasDir = ToolPathResolver.GetSchemasDirectoryPath(config);
         Directory.CreateDirectory(schemasDir);

         var environmentName = ConnectionStringResolver.ResolveEnvironmentName(config, connectionStringOverride, environmentOverride);
         var fileName = environmentName is not null ? $"schema.{environmentName}.sql" : "schema.sql";
         var outputPath = Path.Combine(schemasDir, fileName);

         Console.WriteLine("Connecting to database...");

         await using var connection = new DatabaseConnection(connectionString);

          Console.WriteLine("Extracting schema...");

          var schemaExtractor = new SchemaExtractor(connection);
          var script = await schemaExtractor.GenerateSchemaScriptAsync(cancellationToken);
          var tables = (await schemaExtractor.GetTablesAsync(cancellationToken)).ToArray();
          var constraints = (await schemaExtractor.GetConstraintsAsync(cancellationToken)).ToArray();

          await File.WriteAllTextAsync(outputPath, script, cancellationToken);

          var tablesDirectory = Path.Combine(ToolPathResolver.GetProjectDirectoryPath(config), "Tables");
          Directory.CreateDirectory(tablesDirectory);

          var tableNamespace = NamespaceResolver.Resolve(tablesDirectory);
          var tableDefinitions = TableDefinitionScaffolder.Generate(tableNamespace, tables, constraints);

          foreach (var file in tableDefinitions.Files)
          {
             var filePath = Path.Combine(tablesDirectory, file.FileName);
             await File.WriteAllTextAsync(filePath, file.Content, cancellationToken);
          }

          Console.WriteLine();
          Console.WriteLine($"Schema written to {outputPath}");
          Console.WriteLine($"Generated {tableDefinitions.Files.Count} table definition file(s) in {tablesDirectory}");

          foreach (var warning in tableDefinitions.Warnings)
          {
             Console.WriteLine($"Warning: {warning}");
          }
       });

      return command;
   }
}
