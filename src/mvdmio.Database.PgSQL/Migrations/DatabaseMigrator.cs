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
///    Class for running database migrations. Migrations are tracked per scope: a migration runs when its
///    identifier is ahead of the highest executed identifier within its own scope, so the timelines of
///    different assemblies advance independently.
/// </summary>
[PublicAPI]
public sealed class DatabaseMigrator : IDatabaseMigrator
{
   private const string MIGRATIONS_SCHEMA = "mvdmio";
   private const string MIGRATIONS_TABLE = "migrations";
   private const string MIGRATIONS_TABLE_FULLY_QUALIFIED = "\"mvdmio\".\"migrations\"";

   // Fixed 64-bit key for the session-scoped PostgreSQL advisory lock that serializes migration runs
   // across concurrently-starting instances. The value is the ASCII bytes of "mvdmio\0\1" and is a constant,
   // not derived at runtime, so every instance contends for the same lock. See docs/adr/0001.
   private const long MIGRATION_ADVISORY_LOCK_KEY = 0x6D76_646D_696F_0001;

   private readonly DatabaseConnection _connection;
   private readonly IMigrationRetriever _migrationRetriever;
   private readonly ILogger<DatabaseMigrator> _logger;
   private readonly string? _environment;
   private readonly Assembly[] _assemblies;

   // Caches the positive probe result only: once the scope column exists it never disappears, while a
   // negative result flips as soon as the table is upgraded. Saves an information_schema query per read.
   private bool _scopeColumnExists;

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
   ///    watermark yet.
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
   ///    watermark yet.
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
      // The scope column only exists once this migrator has touched the database; reading from a
      // not-yet-upgraded database must still work, so fall back to a scope-less select in that case.
      if (!await ScopeColumnExistsAsync(cancellationToken))
      {
         return await _connection.Dapper.QueryAsync<ExecutedMigrationModel>(
            $"""
            SELECT
               identifier AS identifier,
               name AS name,
               executed_at AS executedAtUtc
            FROM {MIGRATIONS_TABLE_FULLY_QUALIFIED}
            """,
            ct: cancellationToken
         );
      }

      return await _connection.Dapper.QueryAsync<ExecutedMigrationModel>(
         $"""
         SELECT
            identifier AS identifier,
            name AS name,
            executed_at AS executedAtUtc,
            scope AS scope
         FROM {MIGRATIONS_TABLE_FULLY_QUALIFIED}
         """,
         ct: cancellationToken
      );
   }

   /// <inheritdoc />
   public Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken = default)
   {
      return MigrateAsync(targetIdentifier: null, cancellationToken);
   }

   /// <inheritdoc />
   public Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken = default)
   {
      return MigrateAsync(targetIdentifier, cancellationToken);
   }

   /// <summary>
   ///    Runs the full migration orchestration under a session-scoped advisory lock so that concurrently-starting
   ///    instances apply migrations exactly once. The lock is acquired before the per-scope watermark read, held
   ///    across schema application, the table upgrade, the scope backfill, and the entire migration loop, and
   ///    released in a <c>finally</c>.
   /// </summary>
   private async Task MigrateAsync(long? targetIdentifier, CancellationToken cancellationToken)
   {
      // Open the connection for the whole run so the session-scoped advisory lock stays held across every step.
      // Mirrors the _transactionOpenedConnection pattern: close it only if we were the ones who opened it.
      var migratorOpenedConnection = await _connection.OpenAsync(cancellationToken);
      var lockAcquired = false;

      try
      {
         await AcquireMigrationLockAsync(cancellationToken);
         lockAcquired = true;

         // Discover migrations once for the whole run; schema application needs them to decide which
         // scopes each schema file's assembly vouches for.
         var discoveredMigrations = _migrationRetriever.RetrieveMigrations().ToArray();

         var tableExisted = await _connection.Management.TableExistsAsync(MIGRATIONS_SCHEMA, MIGRATIONS_TABLE);
         ExecutedMigrationModel[] executedMigrations;

         if (tableExisted)
         {
            // Upgrade and backfill before the per-scope empty check so legacy scope-less rows become watermarks
            // and a schema for an already-attributed scope is not re-applied.
            await MigrationsTableManager.EnsureTableAsync(_connection, cancellationToken);
            _scopeColumnExists = true;
            executedMigrations = (await RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
            executedMigrations = await BackfillScopesAsync(executedMigrations, discoveredMigrations, cancellationToken);
         }
         else
         {
            executedMigrations = [];
         }

         var scopesWithWatermark = executedMigrations
            .Where(row => row.Scope is not null)
            .Select(row => row.Scope!)
            .ToHashSet(StringComparer.Ordinal);
         var databaseWasGloballyEmpty = !tableExisted || executedMigrations.Length == 0;

         var schemasApplied = await ApplySchemasForEmptyScopesAsync(
            discoveredMigrations,
            scopesWithWatermark,
            databaseWasGloballyEmpty,
            targetIdentifier,
            cancellationToken);

         if (schemasApplied)
         {
            executedMigrations = (await RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
            executedMigrations = await BackfillScopesAsync(executedMigrations, discoveredMigrations, cancellationToken);
         }
         else if (!tableExisted)
         {
            await MigrationsTableManager.EnsureTableAsync(_connection, cancellationToken);
            _scopeColumnExists = true;
         }

         var pendingMigrations = PendingMigrationSelector.SelectPending(executedMigrations, discoveredMigrations, targetIdentifier);

         foreach (var migration in pendingMigrations)
         {
            await RunAsync(migration, cancellationToken);
         }
      }
      finally
      {
         // Clean up with a non-cancellable token: even when the run is cancelled mid-way, the advisory lock must
         // still be released and the connection closed (passing the original token would make WaitAsync throw and
         // skip cleanup). The lock is held on the session (outside any transaction) so a transaction rollback cannot
         // release it; release it explicitly here, before the conditional close, so it never lingers across the close.
         // Releasing is best-effort: a failure here (e.g. a broken connection) must not mask the exception already
         // propagating, and the session lock is released anyway once the connection is closed/returned to the pool.
         if (lockAcquired)
         {
            try
            {
               await ReleaseMigrationLockAsync(CancellationToken.None);
            }
            catch
            {
               // Intentionally swallowed; see comment above.
            }
         }

         if (migratorOpenedConnection)
            await _connection.CloseAsync(CancellationToken.None);
      }
   }

   /// <inheritdoc />
   public async Task RunAsync(IDbMigration migration, CancellationToken cancellationToken = default)
   {
      try
      {
         await _connection.InTransactionAsync(async () =>
         {
            await migration.UpAsync(_connection);
            await InsertMigrationRowAsync(migration.Identifier, migration.Name, migration.Scope, cancellationToken);
         }
         );
      }
      catch (Exception exception)
      {
         throw new MigrationException(migration, exception);
      }
   }

   /// <summary>
   ///    Records an executed migration. Falls back to a scope-less insert when the table has not been
   ///    upgraded yet (a direct <see cref="RunAsync" /> call against a legacy database, without a prior
   ///    <see cref="MigrateDatabaseToLatestAsync" />); the backfill attributes such rows once the table
   ///    is upgraded.
   /// </summary>
   private async Task InsertMigrationRowAsync(long identifier, string name, string? scope, CancellationToken cancellationToken)
   {
      var parameters = new Dictionary<string, object?> {
         { "identifier", identifier },
         { "name", name },
         { "executedAtUtc", DateTime.UtcNow }
      };

      if (await ScopeColumnExistsAsync(cancellationToken))
      {
         parameters["scope"] = scope;

         await _connection.Dapper.ExecuteAsync(
            $"INSERT INTO {MIGRATIONS_TABLE_FULLY_QUALIFIED} (identifier, name, executed_at, scope) VALUES (:identifier, :name, :executedAtUtc, :scope)",
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

   /// <summary>
   ///    Applies embedded schemas for assemblies that still have at least one empty vouched scope, in
   ///    constructor order. A migrate-to target skips only the schemas whose headers exceed it. Baseline rows
   ///    are recorded for vouched scopes that had no watermark before this run; legacy scope-less header lines
   ///    are recorded only when the database was globally empty.
   /// </summary>
   /// <returns>True when at least one schema file was applied.</returns>
   private async Task<bool> ApplySchemasForEmptyScopesAsync(
      IReadOnlyCollection<IDbMigration> discoveredMigrations,
      IReadOnlySet<string> scopesWithWatermark,
      bool databaseWasGloballyEmpty,
      long? targetIdentifier,
      CancellationToken cancellationToken)
   {
      if (_assemblies.Length == 0)
         return false;

      var schemas = await EmbeddedSchemaDiscovery.ReadAllSchemaContentsAsync(_assemblies, _environment, cancellationToken);

      if (schemas.Count == 0)
         return false;

      var schemasToApply = new List<(string Content, string ResourceName, Assembly Assembly, IReadOnlyList<SchemaFileMigrationInfo> HeaderLines, IReadOnlyCollection<string> VouchedScopes)>();

      foreach (var (content, resourceName, assembly) in schemas)
      {
         if (string.IsNullOrEmpty(content))
            continue;

         var headerLines = SchemaFileParser.ParseMigrationVersion(content);
         var vouchedScopes = GetVouchedScopes(assembly, discoveredMigrations);

         if (!SchemaBootstrapSelector.ShouldApplySchema(vouchedScopes, scopesWithWatermark, headerLines, targetIdentifier))
            continue;

         schemasToApply.Add((content, resourceName, assembly, headerLines, vouchedScopes));
      }

      if (schemasToApply.Count == 0)
         return false;

      await _connection.InTransactionAsync(async () =>
      {
         // Pre-create the migrations table so schema files that also try to create it
         // (with or without IF NOT EXISTS) don't conflict within the same transaction.
         await MigrationsTableManager.EnsureTableAsync(_connection, cancellationToken);
         _scopeColumnExists = true;

         var headers = new List<SchemaFileHeader>();

         foreach (var (content, resourceName, assembly, headerLines, vouchedScopes) in schemasToApply)
         {
            await _connection.Dapper.ExecuteAsync(content, ct: cancellationToken);

            headers.Add(new SchemaFileHeader(
               resourceName,
               assembly.GetName().Name ?? assembly.ToString(),
               headerLines,
               vouchedScopes
            ));
         }

         var selection = SchemaBaselineSelector.SelectBaselines(headers);

         foreach (var rejection in selection.Rejected)
         {
            _logger.LogWarning(
               "Ignoring migration-version header line for scope {Scope} (identifier {Identifier}) in schema resource '{ResourceName}' from assembly '{AssemblyName}': " +
               "the assembly does not vouch for that scope, so no baseline row is recorded and that scope's migrations run from its own watermark. " +
               "Declare scope ownership ('scopes' in .mvdmio-migrations.yml) and re-run 'db pull' to remove foreign scopes from the header.",
               rejection.HeaderLine.Scope,
               rejection.HeaderLine.Identifier,
               rejection.ResourceName,
               rejection.AssemblyName
            );
         }

         var baselinesToRecord = SchemaBootstrapSelector.FilterBaselinesToRecord(
            selection.Baselines,
            scopesWithWatermark,
            databaseWasGloballyEmpty);

         foreach (var baseline in baselinesToRecord)
         {
            await InsertMigrationRowAsync(baseline.Identifier, baseline.Name, baseline.Scope, cancellationToken);
         }
      });

      return true;
   }

   /// <summary>
   ///    The scopes a schema file's assembly vouches for: the scopes of migrations discovered from that
   ///    assembly, plus the assembly's simple name (the default scope, which also covers an assembly that
   ///    folded all of its migrations into its schema and therefore contributes none to discover).
   /// </summary>
   private static IReadOnlyCollection<string> GetVouchedScopes(Assembly assembly, IReadOnlyCollection<IDbMigration> discoveredMigrations)
   {
      var scopes = new HashSet<string>(StringComparer.Ordinal);

      var assemblyName = assembly.GetName().Name;
      if (assemblyName is not null)
         scopes.Add(assemblyName);

      foreach (var migration in discoveredMigrations)
      {
         if (migration.GetType().Assembly == assembly)
            scopes.Add(migration.Scope);
      }

      return scopes;
   }

   /// <summary>
   ///    Temporary upgrade aid: attributes legacy scope-less rows to their scope by matching identifiers
   ///    against the discovered migrations. Fills only rows whose scope is still null, so concurrent runners
   ///    (serialized by the advisory lock) each fill the rows they recognize. Logs a single warning for rows
   ///    no discovered migration claims. Returns the executed set with the attributions applied, so callers
   ///    need no second read. Removed in the next major version.
   /// </summary>
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
            "These rows are excluded from every scope's watermark; set their scope manually in \"mvdmio\".\"migrations\".",
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

   /// <summary>
   ///    Applies all scope attributions in a single statement. The NOT EXISTS guard skips a row whose
   ///    (scope, identifier) pair already exists — possible when an unattributed row's migration re-ran and
   ///    was recorded with its scope — so the backfill can never trip the unique index and brick the run;
   ///    such a row stays scope-less, which is harmless because the scoped row already carries the watermark.
   /// </summary>
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

   /// <summary>
   ///    Acquires the session-scoped advisory lock, blocking until it is granted. Issued with an infinite command
   ///    timeout so Npgsql's default 30-second command timeout cannot abort a wait for a long-but-healthy migration
   ///    held by another instance.
   /// </summary>
   private async Task AcquireMigrationLockAsync(CancellationToken cancellationToken)
   {
      await _connection.Dapper.ExecuteAsync(
         "SELECT pg_advisory_lock(:key)",
         new Dictionary<string, object?> { { "key", MIGRATION_ADVISORY_LOCK_KEY } },
         commandTimeout: TimeSpan.Zero, // 0 = infinite; a healthy migration may legitimately run longer than the default timeout.
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Releases the session-scoped advisory lock acquired by <see cref="AcquireMigrationLockAsync" />.
   /// </summary>
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
}
