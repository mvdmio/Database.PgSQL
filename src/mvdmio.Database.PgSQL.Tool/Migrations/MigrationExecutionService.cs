using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Tool.Migrations;

/// <summary>
///    Executes migrations for the selected target.
/// </summary>
internal class MigrationExecutionService
{
   private readonly IMigrationRuntimeFactory _runtimeFactory;
   private readonly ISchemaResourceService _schemaResourceService;
   private readonly IMigrateReporter _reporter;

   public MigrationExecutionService()
      : this(new DatabaseMigrationRuntimeFactory(), new EmbeddedSchemaResourceService(), new ConsoleMigrateReporter())
   {
   }

   internal MigrationExecutionService(
      IMigrationRuntimeFactory runtimeFactory,
      ISchemaResourceService schemaResourceService,
      IMigrateReporter reporter
   )
   {
      _runtimeFactory = runtimeFactory;
      _schemaResourceService = schemaResourceService;
      _reporter = reporter;
   }

   public virtual async Task ExecuteAsync(
      MigrateRequest request,
      string connectionString,
      string? environmentName,
      MigrationProjectContext project,
      CancellationToken cancellationToken = default
   )
   {
      var targetMigrations = GetTargetMigrations(request, project.Migrations);
      if (targetMigrations is null)
         return;

      _reporter.WriteInfo(string.Empty);

      await using var runtime = _runtimeFactory.Create(connectionString, environmentName, project);
      var isDatabaseEmpty = await runtime.IsDatabaseEmptyAsync(cancellationToken);
      var alreadyExecuted = isDatabaseEmpty
         ? []
         : (await runtime.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();

      if (!await TryReportSchemaPathAsync(request, environmentName, project, isDatabaseEmpty, cancellationToken))
      {
         var pendingCount = targetMigrations.Count(m => alreadyExecuted.All(e => e.Identifier != m.Identifier));

         if (request.IsLatest)
         {
            _reporter.WriteInfo($"Found {targetMigrations.Count} migration(s), {alreadyExecuted.Length} already applied.");

            if (pendingCount == 0)
            {
               _reporter.WriteInfo("Database is already up to date.");
               return;
            }
         }
         else
         {
            _reporter.WriteInfo($"Found {targetMigrations.Count} migration(s) up to {request.TargetIdentifier}, {alreadyExecuted.Length} already applied.");

            if (pendingCount == 0)
            {
               _reporter.WriteInfo("Database is already up to date for the specified target.");
               return;
            }
         }

         _reporter.WriteInfo(string.Empty);
      }

      if (request.IsLatest)
         await runtime.MigrateDatabaseToLatestAsync(cancellationToken);
      else
         await runtime.MigrateDatabaseToAsync(request.TargetIdentifier!.Value, cancellationToken);

      var finalExecuted = (await runtime.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
      var appliedCount = finalExecuted.Length - alreadyExecuted.Length;
      _reporter.WriteInfo($"Migration complete. {appliedCount} migration(s) applied.");
   }

   private IReadOnlyList<IDbMigration>? GetTargetMigrations(
      MigrateRequest request,
      IReadOnlyList<IDbMigration> migrations
   )
   {
      if (request.IsLatest)
         return migrations;

      var filtered = migrations.Where(m => m.Identifier <= request.TargetIdentifier!.Value).ToArray();
      if (filtered.Length > 0)
         return filtered;

      _reporter.WriteError($"Error: No migrations found with identifier <= {request.TargetIdentifier}.");
      return null;
   }

   private async Task<bool> TryReportSchemaPathAsync(
      MigrateRequest request,
      string? environmentName,
      MigrationProjectContext project,
      bool isDatabaseEmpty,
      CancellationToken cancellationToken
   )
   {
      if (!isDatabaseEmpty || !_schemaResourceService.SchemaResourceExists(project, environmentName))
         return false;

      var schemaResourceName = _schemaResourceService.GetSchemaResourceName(project, environmentName);
      var schemaContent = await _schemaResourceService.ReadSchemaContentAsync(project, environmentName, cancellationToken);

      if (request.IsLatest)
      {
         _reporter.WriteInfo($"Empty database detected. Will apply embedded schema: {schemaResourceName}");

         if (schemaContent is not null)
         {
            var migrationInfo = SchemaFileParser.ParseMigrationVersion(schemaContent);

            if (migrationInfo is not null)
               _reporter.WriteInfo($"Schema contains migration version: {migrationInfo.Value.Identifier} ({migrationInfo.Value.Name})");
         }

         _reporter.WriteInfo(string.Empty);
         return true;
      }

      if (schemaContent is null)
         return true;

      var targetIdentifier = request.TargetIdentifier!.Value;
      var schemaMigrationInfo = SchemaFileParser.ParseMigrationVersion(schemaContent);
      if (schemaMigrationInfo is null)
         return true;

      if (schemaMigrationInfo.Value.Identifier <= targetIdentifier)
      {
         _reporter.WriteInfo($"Empty database detected. Will apply embedded schema: {schemaResourceName}");
         _reporter.WriteInfo($"Schema contains migration version: {schemaMigrationInfo.Value.Identifier} ({schemaMigrationInfo.Value.Name})");
         _reporter.WriteInfo(string.Empty);
         return true;
      }

      _reporter.WriteInfo($"Schema version ({schemaMigrationInfo.Value.Identifier}) is newer than target ({targetIdentifier}). Running migrations instead.");
      _reporter.WriteInfo(string.Empty);
      return true;
   }
}

internal interface IMigrateReporter
{
   void WriteInfo(string message);
   void WriteError(string message);
}

internal sealed class ConsoleMigrateReporter : IMigrateReporter
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

internal interface ISchemaResourceService
{
   bool SchemaResourceExists(MigrationProjectContext project, string? environmentName);
   string? GetSchemaResourceName(MigrationProjectContext project, string? environmentName);
   Task<string?> ReadSchemaContentAsync(MigrationProjectContext project, string? environmentName, CancellationToken cancellationToken);
}

internal sealed class EmbeddedSchemaResourceService : ISchemaResourceService
{
   public bool SchemaResourceExists(MigrationProjectContext project, string? environmentName)
   {
      return EmbeddedSchemaDiscovery.SchemaResourceExists([project.Assembly], environmentName);
   }

   public string? GetSchemaResourceName(MigrationProjectContext project, string? environmentName)
   {
      return EmbeddedSchemaDiscovery.GetSchemaResourceName([project.Assembly], environmentName);
   }

   public Task<string?> ReadSchemaContentAsync(MigrationProjectContext project, string? environmentName, CancellationToken cancellationToken)
   {
      return EmbeddedSchemaDiscovery.ReadSchemaContentAsync([project.Assembly], environmentName, cancellationToken);
   }
}

internal interface IMigrationRuntime : IAsyncDisposable
{
   Task<bool> IsDatabaseEmptyAsync(CancellationToken cancellationToken);
   Task<IEnumerable<ExecutedMigrationModel>> RetrieveAlreadyExecutedMigrationsAsync(CancellationToken cancellationToken);
   Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken);
   Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken);
}

internal interface IMigrationRuntimeFactory
{
   IMigrationRuntime Create(string connectionString, string? environmentName, MigrationProjectContext project);
}

internal sealed class DatabaseMigrationRuntimeFactory : IMigrationRuntimeFactory
{
   public IMigrationRuntime Create(string connectionString, string? environmentName, MigrationProjectContext project)
   {
      return new DatabaseMigrationRuntime(connectionString, environmentName, project);
   }
}

internal sealed class DatabaseMigrationRuntime : IMigrationRuntime
{
   private readonly DatabaseConnection _connection;
   private readonly DatabaseMigrator _migrator;

   public DatabaseMigrationRuntime(string connectionString, string? environmentName, MigrationProjectContext project)
   {
      _connection = new DatabaseConnection(connectionString);
      _migrator = new DatabaseMigrator(_connection, environmentName, [project.Assembly], project.MigrationRetriever);
   }

   public async ValueTask DisposeAsync()
   {
      await _connection.DisposeAsync();
   }

   public async Task<bool> IsDatabaseEmptyAsync(CancellationToken cancellationToken)
   {
      return await _migrator.IsDatabaseEmptyAsync(cancellationToken);
   }

   public async Task<IEnumerable<ExecutedMigrationModel>> RetrieveAlreadyExecutedMigrationsAsync(CancellationToken cancellationToken)
   {
      return await _migrator.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken);
   }

   public async Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken)
   {
      await _migrator.MigrateDatabaseToLatestAsync(cancellationToken);
   }

   public async Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken)
   {
      await _migrator.MigrateDatabaseToAsync(targetIdentifier, cancellationToken);
   }
}
