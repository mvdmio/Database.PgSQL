using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Models;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Migrations;
using mvdmio.Database.PgSQL.Tool.Pull;

namespace mvdmio.Database.PgSQL.Tool.Cleanup;

internal sealed class CleanupHandler
{
   private readonly MigrationProjectLoader _projectLoader;
   private readonly SchemaExportService _schemaExportService;
   private readonly IMigrationRuntimeFactory _runtimeFactory;
   private readonly ICleanupReporter _reporter;

   public CleanupHandler()
      : this(new MigrationProjectLoader(), new SchemaExportService(), new DatabaseMigrationRuntimeFactory(), new ConsoleCleanupReporter())
   {
   }

   internal CleanupHandler(
      MigrationProjectLoader projectLoader,
      SchemaExportService schemaExportService,
      IMigrationRuntimeFactory runtimeFactory,
      ICleanupReporter reporter
   )
   {
      _projectLoader = projectLoader;
      _schemaExportService = schemaExportService;
      _runtimeFactory = runtimeFactory;
      _reporter = reporter;
   }

   public Task<int> HandleAsync(CancellationToken cancellationToken = default)
   {
      var config = ToolConfigurationLoader.Load();
      return HandleAsync(config, cancellationToken);
   }

   internal async Task<int> HandleAsync(ToolConfiguration config, CancellationToken cancellationToken = default)
   {
      if (config.ConnectionStrings is null || config.ConnectionStrings.Count == 0)
      {
         _reporter.WriteError("Error: No environments configured.");
         _reporter.WriteError("Add connectionStrings to .mvdmio-migrations.yml before running cleanup.");
         return ExitCodes.FAILURE;
      }

      var schemasDirectory = ToolPathResolver.GetSchemasDirectoryPath(config);
      var migrationsDirectory = ToolPathResolver.GetMigrationsDirectoryPath(config);
      Directory.CreateDirectory(schemasDirectory);

      var project = await _projectLoader.LoadAsync(ToolPathResolver.GetProjectPath(config), cancellationToken);
      var environments = new List<CleanupEnvironment>();

      foreach (KeyValuePair<string, string> environment in config.ConnectionStrings)
      {
         var cleanupEnvironment = await PullEnvironmentAsync(config, environment.Key, environment.Value, schemasDirectory, project, cancellationToken);

         if (cleanupEnvironment is null)
            return ExitCodes.FAILURE;

         environments.Add(cleanupEnvironment);
      }

      _reporter.WriteInfo(string.Empty);

      var plan = MigrationCleanupPlanner.Plan(migrationsDirectory, project.Migrations, environments);
      ApplyPlan(plan);
      return ExitCodes.SUCCESS;
   }

   private async Task<CleanupEnvironment?> PullEnvironmentAsync(
      ToolConfiguration config,
      string environmentName,
      string connectionString,
      string schemasDirectory,
      MigrationProjectContext project,
      CancellationToken cancellationToken
   )
   {
      var schemaPath = Path.Combine(schemasDirectory, $"schema.{environmentName}.sql");

      _reporter.WriteInfo($"Pulling schema for '{environmentName}'...");

      SchemaExportResult schemaResult;

      try
      {
         schemaResult = await _schemaExportService.ExportAsync(connectionString, config.Schemas, config.Scopes, cancellationToken);
      }
      catch (InvalidOperationException ex)
      {
         _reporter.WriteError($"Error: {ex.Message}");
         return null;
      }

      var script = schemaResult.Script;
      await File.WriteAllTextAsync(schemaPath, script, cancellationToken);

      foreach (var warning in schemaResult.Warnings)
         _reporter.WriteInfo($"  Warning: {warning}");

      var migrationInfos = SchemaFileParser.ParseMigrationVersion(script);
      var lowestMigrationInfo = migrationInfos.Count == 0
         ? (SchemaFileMigrationInfo?)null
         : migrationInfos.MinBy(x => x.Identifier);

      await using var runtime = _runtimeFactory.Create(connectionString, environmentName, project);
      var executedMigrations = await runtime.RetrieveRecordedMigrationsAsync(cancellationToken);

      if (lowestMigrationInfo is null)
         _reporter.WriteInfo($"  Wrote {schemaPath} (no recorded migration version)");
      else
         _reporter.WriteInfo($"  Wrote {schemaPath} (migration {lowestMigrationInfo.Value.Identifier}: {lowestMigrationInfo.Value.Name})");

      return new CleanupEnvironment(environmentName, lowestMigrationInfo?.Identifier, executedMigrations);
   }

   private void ApplyPlan(MigrationCleanupPlan plan)
   {
      if (plan.SkipReason is not null)
      {
         _reporter.WriteInfo($"Cleanup skipped: {plan.SkipReason}");
         _reporter.WriteInfo("No migration files were deleted.");
         return;
      }

      _reporter.WriteInfo($"Lowest migration version across environments: {plan.LowestMigrationIdentifier}");

      foreach (var pendingFile in plan.PendingFilesKept)
         _reporter.WriteInfo($"Kept {pendingFile.Path}: still pending in {string.Join(", ", pendingFile.PendingEnvironments)}");

      if (plan.FilesToDelete.Length == 0)
      {
         _reporter.WriteInfo(plan.PendingFilesKept.Length == 0
            ? "No migration files are older than the lowest environment version."
            : "No migration files were deleted.");
         return;
      }

      foreach (var file in plan.FilesToDelete)
      {
         File.Delete(file);
         _reporter.WriteInfo($"Deleted {file}");
      }

      _reporter.WriteInfo(string.Empty);
      _reporter.WriteInfo($"Cleanup complete. Deleted {plan.FilesToDelete.Length} migration file(s).");
   }
}

internal interface ICleanupReporter
{
   void WriteInfo(string message);
   void WriteError(string message);
}

internal sealed class ConsoleCleanupReporter : ICleanupReporter
{
   public void WriteInfo(string message)
   {
      Console.WriteLine(message);
   }

   public void WriteError(string message)
   {
      Console.Error.WriteLine(message);
   }
}
