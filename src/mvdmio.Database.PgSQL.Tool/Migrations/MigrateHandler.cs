using mvdmio.Database.PgSQL.Tool.Configuration;

namespace mvdmio.Database.PgSQL.Tool.Migrations;

/// <summary>
///    Orchestrates migrate command execution.
/// </summary>
internal sealed class MigrateHandler
{
   private readonly MigrationProjectLoader _projectLoader;
   private readonly MigrationExecutionService _executionService;
   private readonly IMigrateReporter _reporter;

   public MigrateHandler()
      : this(new MigrationProjectLoader(), new MigrationExecutionService(), new ConsoleMigrateReporter())
   {
   }

   internal MigrateHandler(
      MigrationProjectLoader projectLoader,
      MigrationExecutionService executionService,
      IMigrateReporter reporter
   )
   {
      _projectLoader = projectLoader;
      _executionService = executionService;
      _reporter = reporter;
   }

   public Task HandleAsync(
      MigrateRequest request,
      string? connectionStringOverride,
      string? environmentOverride,
      CancellationToken cancellationToken = default
   )
   {
      var config = ToolConfigurationLoader.Load();
      return HandleAsync(request, config, connectionStringOverride, environmentOverride, cancellationToken);
   }

   internal async Task HandleAsync(
      MigrateRequest request,
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

      var projectPath = ToolPathResolver.GetProjectPath(config);
      var project = _projectLoader.Load(projectPath);
      var environmentName = ConnectionStringResolver.ResolveEnvironmentName(config, connectionStringOverride, environmentOverride);

      await _executionService.ExecuteAsync(request, connectionString, environmentName, project, cancellationToken);
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
