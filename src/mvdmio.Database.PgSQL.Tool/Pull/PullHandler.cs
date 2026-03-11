using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using mvdmio.Database.PgSQL.Tool.Configuration;

namespace mvdmio.Database.PgSQL.Tool.Pull;

/// <summary>
///    Orchestrates the db pull workflow.
/// </summary>
internal sealed class PullHandler
{
   private readonly SchemaExportService _schemaExportService;
   private readonly TableDefinitionWriter _tableDefinitionWriter;
   private readonly IPullFileSystem _fileSystem;
   private readonly IPullReporter _reporter;

   public PullHandler()
      : this(new SchemaExportService(), new TableDefinitionWriter(), new PullFileSystem(), new ConsolePullReporter())
   {
   }

   internal PullHandler(
      SchemaExportService schemaExportService,
      TableDefinitionWriter tableDefinitionWriter,
      IPullFileSystem fileSystem,
      IPullReporter reporter
   )
   {
      _schemaExportService = schemaExportService;
      _tableDefinitionWriter = tableDefinitionWriter;
      _fileSystem = fileSystem;
      _reporter = reporter;
   }

   public Task HandleAsync(string? connectionStringOverride, string? environmentOverride, CancellationToken cancellationToken = default)
   {
      var config = ToolConfigurationLoader.Load();
      return HandleAsync(config, connectionStringOverride, environmentOverride, cancellationToken);
   }

   internal async Task HandleAsync(
      ToolConfiguration config,
      string? connectionStringOverride,
      string? environmentOverride,
      CancellationToken cancellationToken = default
   )
   {
      var connectionString = ConnectionStringResolver.ResolveConnectionString(config, connectionStringOverride, environmentOverride);

      if (string.IsNullOrWhiteSpace(connectionString))
      {
         WriteMissingConnectionError(config, environmentOverride);
         return;
      }

      var schemasDirectory = ToolPathResolver.GetSchemasDirectoryPath(config);
      _fileSystem.CreateDirectory(schemasDirectory);

      var environmentName = ConnectionStringResolver.ResolveEnvironmentName(config, connectionStringOverride, environmentOverride);
      var fileName = environmentName is not null ? $"schema.{environmentName}.sql" : "schema.sql";
      var outputPath = Path.Combine(schemasDirectory, fileName);

      _reporter.WriteInfo("Connecting to database...");
      _reporter.WriteInfo("Extracting schema...");

      var schemaResult = await _schemaExportService.ExportAsync(connectionString, cancellationToken);
      await _fileSystem.WriteAllTextAsync(outputPath, schemaResult.Script, cancellationToken);

      var tableDefinitions = await _tableDefinitionWriter.WriteAsync(config, schemaResult.Tables, schemaResult.Constraints, cancellationToken);

      _reporter.WriteInfo(string.Empty);
      _reporter.WriteInfo($"Schema written to {outputPath}");
      _reporter.WriteInfo($"Generated {tableDefinitions.GeneratedFileCount} table definition file(s) in {tableDefinitions.TablesDirectory}");

      foreach (var warning in tableDefinitions.Warnings)
      {
         _reporter.WriteWarning(warning);
      }
   }

   private void WriteMissingConnectionError(ToolConfiguration config, string? environmentOverride)
   {
      if (environmentOverride is not null)
      {
         var available = ConnectionStringResolver.GetAvailableEnvironments(config);
         _reporter.WriteError($"Error: Environment '{environmentOverride}' not found in .mvdmio-migrations.yml.");

         if (available.Length > 0)
            _reporter.WriteError($"Available environments: {string.Join(", ", available)}");

         return;
      }

      _reporter.WriteError("Error: No connection string provided.");
      _reporter.WriteError("Specify one via --connection-string, --environment, or add an entry to connectionStrings in .mvdmio-migrations.yml.");
   }
}

internal interface IPullReporter
{
   void WriteInfo(string message);
   void WriteWarning(string message);
   void WriteError(string message);
}

internal sealed class ConsolePullReporter : IPullReporter
{
   public void WriteInfo(string message)
   {
      Console.WriteLine(message);
   }

   public void WriteWarning(string message)
   {
      Console.WriteLine($"Warning: {message}");
   }

   public void WriteError(string message)
   {
      Console.Error.WriteLine(message);
   }
}

internal interface IPullFileSystem
{
   void CreateDirectory(string path);
   Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken);
}

internal sealed class PullFileSystem : IPullFileSystem
{
   public void CreateDirectory(string path)
   {
      Directory.CreateDirectory(path);
   }

   public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
   {
      return File.WriteAllTextAsync(path, contents, cancellationToken);
   }
}
