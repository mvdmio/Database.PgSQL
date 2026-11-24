using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Class for running database migrations. Migrations are tracked per scope, so the timelines of different
///    assemblies advance independently. <see cref="IDatabaseMigrator.MigrateDatabaseToLatestAsync(DeployPhase, CancellationToken)" /> states which
///    migrations are pending.
/// </summary>
[PublicAPI]
public sealed class DatabaseMigrator : IDatabaseMigrator
{
   private const string MIGRATIONS_SCHEMA = "mvdmio";
   private const string MIGRATIONS_TABLE = "migrations";
   private const string MIGRATIONS_TABLE_FULLY_QUALIFIED = "\"mvdmio\".\"migrations\"";

   private const long MIGRATION_ADVISORY_LOCK_KEY = 0x6D76_646D_696F_0001;

   private readonly DatabaseConnection _connection;
   private readonly IMigrationRetriever _migrationRetriever;
   private readonly ILogger<DatabaseMigrator> _logger;
   private readonly string? _environment;
   private readonly Assembly[] _assemblies;

   private bool _scopeColumnExists;
   private bool _phaseColumnExists;

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class using reflection-based migration retrieval.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes. These assemblies are also searched for embedded schema resources.
   /// </param>
   [Obsolete(message: "Use the constructor that accepts an ILoggerFactory; in the 1.0.0 release the loggerFactory parameter becomes required.")]
   public DatabaseMigrator(DatabaseConnection connection, params Assembly[] assembliesContainingMigrations)
      : this(connection, environment: null, NullLoggerFactory.Instance, assembliesContainingMigrations)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class using reflection-based migration retrieval.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="loggerFactory">The logger factory to use for logging migration warnings and diagnostics.</param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes. These assemblies are also searched for embedded schema resources.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, ILoggerFactory loggerFactory, params Assembly[] assembliesContainingMigrations)
      : this(connection, environment: null, loggerFactory, assembliesContainingMigrations)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class using reflection-based migration retrieval
   ///    with environment-based schema discovery.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="environment">
   ///    Optional environment name for schema discovery. If specified, looks for an embedded
   ///    schema.{environment}.sql resource (case-insensitive). Falls back to schema.sql if not found.
   ///    An assembly's embedded schema is applied when at least one scope that assembly vouches for has no
   ///    rows yet.
   /// </param>
   /// <param name="loggerFactory">The logger factory to use for logging migration warnings and diagnostics.</param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes. These assemblies are also searched for embedded schema resources.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, string? environment, ILoggerFactory loggerFactory, params Assembly[] assembliesContainingMigrations)
      : this(connection, environment, loggerFactory, assembliesContainingMigrations, new ReflectionMigrationRetriever(assembliesContainingMigrations))
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class with a custom migration retriever.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="loggerFactory">The logger factory to use for logging migration warnings and diagnostics.</param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(DatabaseConnection connection, ILoggerFactory loggerFactory, IMigrationRetriever migrationRetriever)
      : this(connection, environment: null, loggerFactory, [], migrationRetriever)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class with a custom migration retriever
   ///    and environment-based schema discovery.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="environment">
   ///    Optional environment name for schema discovery. If specified, looks for an embedded
   ///    schema.{environment}.sql resource (case-insensitive). Falls back to schema.sql if not found.
   ///    An assembly's embedded schema is applied when at least one scope that assembly vouches for has no
   ///    rows yet.
   /// </param>
   /// <param name="loggerFactory">The logger factory to use for logging migration warnings and diagnostics.</param>
   /// <param name="assembliesForSchemaDiscovery">
   ///    Assemblies to search for embedded schema resources. Pass empty array if schema discovery is not needed.
   /// </param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(
      DatabaseConnection connection,
      string? environment,
      ILoggerFactory loggerFactory,
      Assembly[] assembliesForSchemaDiscovery,
      IMigrationRetriever migrationRetriever)
   {
      _connection = connection;
      _environment = environment;
      _logger = loggerFactory.CreateLogger<DatabaseMigrator>();
      _assemblies = assembliesForSchemaDiscovery;
      _migrationRetriever = migrationRetriever;
   }

   /// <inheritdoc />
   public async Task<IEnumerable<ExecutedMigrationModel>> RetrieveAlreadyExecutedMigrationsAsync(CancellationToken cancellationToken = default)
   {
      var scopeColumn = await ScopeColumnExistsAsync(cancellationToken) ? ", scope AS scope" : string.Empty;
      var phaseExpression = await PhaseColumnExistsAsync(cancellationToken)
         ? $"CASE phase WHEN '{MigrationsTableManager.POST_DEPLOY_PHASE_TEXT}' THEN {(int)DeployPhase.PostDeploy} ELSE {(int)DeployPhase.PreDeploy} END"
         : ((int)DeployPhase.PreDeploy).ToString();
      var phaseRecordedExpression = await PhaseColumnExistsAsync(cancellationToken) ? "phase IS NOT NULL" : "FALSE";

      return await _connection.Dapper.QueryAsync<ExecutedMigrationModel>(
         $"""
         SELECT
            identifier AS identifier,
            name AS name,
            executed_at AS executedAtUtc{scopeColumn},
            {phaseExpression} AS phase,
            {phaseRecordedExpression} AS hasRecordedPhase
         FROM {MIGRATIONS_TABLE_FULLY_QUALIFIED}
         """,
         ct: cancellationToken
      );
   }

   /// <inheritdoc />
   public Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken = default)
   {
      return MigrateAsync(targetIdentifier: null, DeployPhase.PreDeploy, cancellationToken);
   }

   /// <inheritdoc />
   public Task MigrateDatabaseToLatestAsync(DeployPhase phase, CancellationToken cancellationToken = default)
   {
      return MigrateAsync(targetIdentifier: null, phase, cancellationToken);
   }

   /// <inheritdoc />
   public Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken = default)
   {
      return MigrateAsync(targetIdentifier, DeployPhase.PreDeploy, cancellationToken);
   }

   /// <inheritdoc />
   public Task MigrateDatabaseToAsync(long targetIdentifier, DeployPhase phase, CancellationToken cancellationToken = default)
   {
      return MigrateAsync(targetIdentifier, phase, cancellationToken);
   }

   private async Task MigrateAsync(long? targetIdentifier, DeployPhase phase, CancellationToken cancellationToken)
   {
      DeployPhaseValidation.ThrowIfInvalid(phase);

      var migratorOpenedConnection = await _connection.OpenAsync(cancellationToken);
      var lockAcquired = false;

      try
      {
         await AcquireMigrationLockAsync(cancellationToken);
         lockAcquired = true;

         var discoveredMigrations = _migrationRetriever.RetrieveMigrations().ToArray();

         var tableExisted = await _connection.Management.TableExistsAsync(MIGRATIONS_SCHEMA, MIGRATIONS_TABLE);
         ExecutedMigrationModel[] executedMigrations;

         if (tableExisted)
         {
            await MigrationsTableManager.EnsureTableAsync(_connection, cancellationToken);
            _scopeColumnExists = true;
            _phaseColumnExists = true;
            executedMigrations = (await RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
            executedMigrations = await BackfillScopesAsync(executedMigrations, discoveredMigrations, cancellationToken);
         }
         else
         {
            executedMigrations = [];
         }

         var scopesWithWatermark = SchemaBootstrapSelector.GetScopesWithWatermark(executedMigrations);
         var databaseWasGloballyEmpty = !tableExisted || executedMigrations.Length == 0;

         cancellationToken.ThrowIfCancellationRequested();

         var schemasApplied = await SchemaBootstrapApplier.ApplyForEmptyScopesAsync(
            _connection,
            _logger,
            _assemblies,
            _environment,
            discoveredMigrations,
            scopesWithWatermark,
            databaseWasGloballyEmpty,
            targetIdentifier,
            CancellationToken.None);

         if (schemasApplied)
         {
            _scopeColumnExists = true;
            _phaseColumnExists = true;
            executedMigrations = (await RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
            executedMigrations = await BackfillScopesAsync(executedMigrations, discoveredMigrations, cancellationToken);
         }
         else if (!tableExisted)
         {
            await MigrationsTableManager.EnsureTableAsync(_connection, cancellationToken);
            _scopeColumnExists = true;
            _phaseColumnExists = true;
         }

         if (phase == DeployPhase.PreDeploy)
            MigrationWarnings.LogWaitingPostDeployParts(_logger, PendingMigrationSelector.SelectWaitingPostDeployParts(executedMigrations, discoveredMigrations, targetIdentifier));

         if (phase == DeployPhase.PostDeploy)
            MigrationWarnings.LogBlockedPostDeployParts(_logger, PendingMigrationSelector.SelectBlockedPostDeployParts(executedMigrations, discoveredMigrations, targetIdentifier));

         var pendingParts = PendingMigrationSelector.SelectPending(executedMigrations, discoveredMigrations, phase, targetIdentifier);

         foreach (var pending in pendingParts)
         {
            if (pending.IsOutOfOrder)
               MigrationWarnings.LogOutOfOrder(_logger, pending);

            await RunPartAsync(pending.Migration, pending.Phase, cancellationToken);
         }
      }
      finally
      {
         if (lockAcquired)
         {
            try
            {
               await ReleaseMigrationLockAsync(CancellationToken.None);
            }
            catch
            {
            }
         }

         if (migratorOpenedConnection)
            await _connection.CloseAsync(CancellationToken.None);
      }
   }

   /// <inheritdoc />
   public Task RunAsync(IDbMigration migration, CancellationToken cancellationToken = default)
   {
      return RunAsync(migration, DeployPhase.PreDeploy, cancellationToken);
   }

   /// <summary>
   ///    Run the parts of a migration that belong to the requested phase and record a history row for each part
   ///    that ran. A part the migration does not have (<see cref="DeployPhase.PostDeploy" /> on a migration that does
   ///    not implement <see cref="IDbMigration.PostDeployAsync" />) is skipped and records no row.
   ///    <see cref="DeployPhase.All" /> runs the pre-deploy part, then the post-deploy part when there is one.
   ///    A token cancelled before a part starts throws <see cref="OperationCanceledException" /> and runs nothing
   ///    further; a part that has started finishes and records its row.
   /// </summary>
   /// <param name="migration">The migration to run.</param>
   /// <param name="phase">The phase to run: <see cref="DeployPhase.PreDeploy" />, <see cref="DeployPhase.PostDeploy" /> or <see cref="DeployPhase.All" />.</param>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <exception cref="ArgumentOutOfRangeException">The phase is zero or has a bit outside <see cref="DeployPhase.All" />.</exception>
   /// <exception cref="MigrationException">A part failed.</exception>
   public async Task RunAsync(IDbMigration migration, DeployPhase phase, CancellationToken cancellationToken = default)
   {
      DeployPhaseValidation.ThrowIfInvalid(phase);

      if (phase.HasFlag(DeployPhase.PreDeploy))
         await RunPartAsync(migration, DeployPhase.PreDeploy, cancellationToken);

      if (phase.HasFlag(DeployPhase.PostDeploy) && PostDeployPart.Exists(migration))
         await RunPartAsync(migration, DeployPhase.PostDeploy, cancellationToken);
   }

   private async Task RunPartAsync(IDbMigration migration, DeployPhase part, CancellationToken cancellationToken)
   {
      cancellationToken.ThrowIfCancellationRequested();

      try
      {
         async Task RunAndRecordAsync()
         {
            await (part == DeployPhase.PreDeploy ? migration.PreDeployAsync(_connection) : migration.PostDeployAsync(_connection));
            await InsertMigrationRowAsync(migration.Identifier, migration.Name, migration.Scope, part, CancellationToken.None);
         }

         if (migration.RunsInTransaction)
            await _connection.InTransactionAsync(RunAndRecordAsync);
         else
            await RunAndRecordAsync();
      }
      catch (Exception exception)
      {
         throw new MigrationException(migration, exception);
      }
   }

   private async Task InsertMigrationRowAsync(long identifier, string name, string? scope, DeployPhase part, CancellationToken cancellationToken)
   {
      var parameters = new Dictionary<string, object?> {
         { "identifier", identifier },
         { "name", name },
         { "executedAtUtc", DateTime.UtcNow }
      };

      if (await ScopeColumnExistsAsync(cancellationToken))
      {
         parameters["scope"] = scope;

         var phaseColumn = string.Empty;
         var phaseValue = string.Empty;

         if (await PhaseColumnExistsAsync(cancellationToken))
         {
            parameters["phase"] = part == DeployPhase.PreDeploy ? MigrationsTableManager.PRE_DEPLOY_PHASE_TEXT : MigrationsTableManager.POST_DEPLOY_PHASE_TEXT;
            phaseColumn = ", phase";
            phaseValue = ", :phase";
         }

         await _connection.Dapper.ExecuteAsync(
            $"INSERT INTO {MIGRATIONS_TABLE_FULLY_QUALIFIED} (identifier, name, executed_at, scope{phaseColumn}) VALUES (:identifier, :name, :executedAtUtc, :scope{phaseValue})",
            parameters,
            ct: cancellationToken
         );

         return;
      }

      await _connection.Dapper.ExecuteAsync(
         $"INSERT INTO {MIGRATIONS_TABLE_FULLY_QUALIFIED} (identifier, name, executed_at) VALUES (:identifier, :name, :executedAtUtc)",
         parameters,
         ct: cancellationToken
      );
   }

   /// <inheritdoc />
   public async Task<bool> IsDatabaseEmptyAsync(CancellationToken cancellationToken = default)
   {
      var tableExists = await _connection.Management.TableExistsAsync(MIGRATIONS_SCHEMA, MIGRATIONS_TABLE);

      if (!tableExists)
         return true;

      var count = await _connection.Dapper.ExecuteScalarAsync<long>(
         $"SELECT COUNT(*) FROM {MIGRATIONS_TABLE_FULLY_QUALIFIED}",
         ct: cancellationToken
      );

      return count == 0;
   }

   private async Task<ExecutedMigrationModel[]> BackfillScopesAsync(
      ExecutedMigrationModel[] executedMigrations,
      IReadOnlyCollection<IDbMigration> discoveredMigrations,
      CancellationToken cancellationToken)
   {
      if (executedMigrations.All(x => x.Scope is not null))
         return executedMigrations;

#pragma warning disable CS0618 // The backfill is obsolete by design; this call site is removed with it in the next major version.
      var result = ScopeBackfillMatcher.Match(executedMigrations, discoveredMigrations);
#pragma warning restore CS0618

      if (result.Assignments.Count > 0)
         await ApplyScopeAssignmentsAsync(result.Assignments, cancellationToken);

      if (result.Unattributed.Count > 0)
      {
         _logger.LogWarning(
            "Could not attribute {UnattributedCount} executed migration row(s) to a scope: {UnattributedRows}. " +
            "These rows count toward no scope's baseline or watermark; set their scope manually in \"mvdmio\".\"migrations\".",
            result.Unattributed.Count,
            string.Join(", ", result.Unattributed.Select(x => $"{x.Identifier} ({x.Name})"))
         );
      }

      var scopeByIdentifier = result.Assignments.ToDictionary(x => x.Identifier, x => x.Scope);

      return executedMigrations
         .Select(row => row.Scope is null && scopeByIdentifier.TryGetValue(row.Identifier, out var scope)
            ? row with { Scope = scope }
            : row)
         .ToArray();
   }

   private async Task ApplyScopeAssignmentsAsync(IReadOnlyList<ScopeAssignment> assignments, CancellationToken cancellationToken)
   {
      var parameters = new Dictionary<string, object?>();
      var valueRows = new List<string>(assignments.Count);

      for (var i = 0; i < assignments.Count; i++)
      {
         valueRows.Add($"(:scope{i}, :identifier{i})");
         parameters[$"scope{i}"] = assignments[i].Scope;
         parameters[$"identifier{i}"] = assignments[i].Identifier;
      }

      await _connection.Dapper.ExecuteAsync(
         $"""
         UPDATE {MIGRATIONS_TABLE_FULLY_QUALIFIED} AS migration_row
         SET scope = assignment.scope
         FROM (VALUES {string.Join(", ", valueRows)}) AS assignment (scope, identifier)
         WHERE migration_row.scope IS NULL
           AND migration_row.identifier = assignment.identifier
           AND NOT EXISTS (
              SELECT 1 FROM {MIGRATIONS_TABLE_FULLY_QUALIFIED} AS existing
              WHERE existing.scope = assignment.scope AND existing.identifier = assignment.identifier
           )
         """,
         parameters,
         ct: cancellationToken
      );
   }

   private async Task AcquireMigrationLockAsync(CancellationToken cancellationToken)
   {
      await _connection.Dapper.ExecuteAsync(
         "SELECT pg_advisory_lock(:key)",
         new Dictionary<string, object?> { { "key", MIGRATION_ADVISORY_LOCK_KEY } },
         commandTimeout: TimeSpan.Zero,
         ct: cancellationToken
      );
   }

   private async Task ReleaseMigrationLockAsync(CancellationToken cancellationToken)
   {
      await _connection.Dapper.ExecuteAsync(
         "SELECT pg_advisory_unlock(:key)",
         new Dictionary<string, object?> { { "key", MIGRATION_ADVISORY_LOCK_KEY } },
         ct: cancellationToken
      );
   }

   private async Task<bool> ScopeColumnExistsAsync(CancellationToken cancellationToken)
   {
      if (_scopeColumnExists)
         return true;

      _scopeColumnExists = await MigrationsTableManager.ScopeColumnExistsAsync(_connection, cancellationToken);
      return _scopeColumnExists;
   }

   private async Task<bool> PhaseColumnExistsAsync(CancellationToken cancellationToken)
   {
      if (_phaseColumnExists)
         return true;

      _phaseColumnExists = await MigrationsTableManager.PhaseColumnExistsAsync(_connection, cancellationToken);
      return _phaseColumnExists;
   }
}
