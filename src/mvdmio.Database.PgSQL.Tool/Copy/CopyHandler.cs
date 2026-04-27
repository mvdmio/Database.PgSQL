using mvdmio.Database.PgSQL.Tool.Configuration;

namespace mvdmio.Database.PgSQL.Tool.Copy;

/// <summary>
///    Orchestrates the db copy workflow.
/// </summary>
internal sealed class CopyHandler
{
   private readonly CopyService _copyService;
   private readonly ICopyReporter _reporter;

   public CopyHandler()
      : this(new CopyService(), new ConsoleCopyReporter())
   {
   }

   internal CopyHandler(CopyService copyService, ICopyReporter reporter)
   {
      _copyService = copyService;
      _reporter = reporter;
   }

   public Task HandleAsync(string fromEnvironment, string toEnvironment, IReadOnlyCollection<string>? schemasOverride, IReadOnlyCollection<string>? excludeTables, CancellationToken cancellationToken = default)
   {
      var config = ToolConfigurationLoader.Load();
      return HandleAsync(config, fromEnvironment, toEnvironment, schemasOverride, excludeTables, cancellationToken);
   }

   internal async Task HandleAsync(
      ToolConfiguration config,
      string fromEnvironment,
      string toEnvironment,
      IReadOnlyCollection<string>? schemasOverride,
      IReadOnlyCollection<string>? excludeTables,
      CancellationToken cancellationToken = default
   )
   {
      var sourceConnectionString = ConnectionStringResolver.ResolveConnectionString(config, null, fromEnvironment);
      if (string.IsNullOrWhiteSpace(sourceConnectionString))
      {
         WriteUnknownEnvironmentError(config, fromEnvironment, "source");
         return;
      }

      var destinationConnectionString = ConnectionStringResolver.ResolveConnectionString(config, null, toEnvironment);
      if (string.IsNullOrWhiteSpace(destinationConnectionString))
      {
         WriteUnknownEnvironmentError(config, toEnvironment, "destination");
         return;
      }

      if (string.Equals(sourceConnectionString, destinationConnectionString, StringComparison.Ordinal))
      {
         _reporter.WriteError("Error: Source and destination resolve to the same connection string. Refusing to copy a database onto itself.");
         return;
      }

      var schemas = schemasOverride is { Count: > 0 } ? schemasOverride : config.Schemas;

      _reporter.WriteInfo($"Copying data from '{fromEnvironment}' to '{toEnvironment}'...");
      if (schemas is { Count: > 0 })
         _reporter.WriteInfo($"Schemas: {string.Join(", ", schemas)}");
      else
         _reporter.WriteInfo("Schemas: (all user schemas)");

      _reporter.WriteInfo(string.Empty);

      CopyResult result;
      try
      {
         result = await _copyService.CopyAsync(
            sourceConnectionString,
            destinationConnectionString,
            schemas,
            excludeTables,
            _reporter,
            cancellationToken
         );
      }
      catch (InvalidOperationException ex)
      {
         _reporter.WriteError($"Error: {ex.Message}");
         return;
      }

      _reporter.WriteInfo(string.Empty);
      _reporter.WriteInfo($"Done. {result.Tables.Count} table(s), {result.TotalRows} row(s), {result.TotalBytes} byte(s).");
   }

   private void WriteUnknownEnvironmentError(ToolConfiguration config, string environmentName, string role)
   {
      var available = ConnectionStringResolver.GetAvailableEnvironments(config);
      _reporter.WriteError($"Error: {role} environment '{environmentName}' not found in .mvdmio-migrations.yml.");

      if (available.Length > 0)
         _reporter.WriteError($"Available environments: {string.Join(", ", available)}");
   }
}
