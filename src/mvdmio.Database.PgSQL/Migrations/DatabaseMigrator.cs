using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Class for running database migrations.
/// </summary>
[PublicAPI]
public sealed class DatabaseMigrator : IDatabaseMigrator
{
   private const string MIGRATIONS_SCHEMA = "mvdmio";
   private const string MIGRATIONS_TABLE = "migrations";
   private const string MIGRATIONS_TABLE_FULLY_QUALIFIED = "\"mvdmio\".\"migrations\"";

   private readonly DatabaseConnection _connection;
   private readonly IMigrationRetriever _migrationRetriever;
   private readonly string? _environment;
   private readonly Assembly[] _assemblies;

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class using reflection-based migration retrieval.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes. These assemblies are also searched for embedded schema resources.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, params Assembly[] assembliesContainingMigrations)
      : this(connection, environment: null, assembliesContainingMigrations)
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
   ///    If the database is empty, the embedded schema is applied before running migrations.
   /// </param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes. These assemblies are also searched for embedded schema resources.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, string? environment, params Assembly[] assembliesContainingMigrations)
      : this(connection, environment, assembliesContainingMigrations, new ReflectionMigrationRetriever(assembliesContainingMigrations))
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class with a custom migration retriever.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(DatabaseConnection connection, IMigrationRetriever migrationRetriever)
      : this(connection, environment: null, [], migrationRetriever)
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
   ///    If the database is empty, the embedded schema is applied before running migrations.
   /// </param>
   /// <param name="assembliesForSchemaDiscovery">
   ///    Assemblies to search for embedded schema resources. Pass empty array if schema discovery is not needed.
   /// </param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(
      DatabaseConnection connection,
      string? environment,
      Assembly[] assembliesForSchemaDiscovery,
      IMigrationRetriever migrationRetriever)
   {
      _connection = connection;
      _environment = environment;
      _assemblies = assembliesForSchemaDiscovery;
      _migrationRetriever = migrationRetriever;
   }

   /// <inheritdoc />
   public async Task<IEnumerable<ExecutedMigrationModel>> RetrieveAlreadyExecutedMigrationsAsync(CancellationToken cancellationToken = default)
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

   /// <inheritdoc />
   public async Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken = default)
   {
      // Check if database is empty and we have a schema resource to apply
      if (await ShouldApplySchemaAsync(targetIdentifier: null, cancellationToken))
      {
         await ApplySchemaAsync(cancellationToken);
      }
      else
      {
         await EnsureMigrationTableExistsAsync();
      }

      await RunPendingMigrationsAsync(targetIdentifier: null, cancellationToken);
   }

   /// <inheritdoc />
   public async Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken = default)
   {
      // Check if database is empty and we have a schema resource to apply
      if (await ShouldApplySchemaAsync(targetIdentifier, cancellationToken))
      {
         await ApplySchemaAsync(cancellationToken);
      }
      else
      {
         await EnsureMigrationTableExistsAsync();
      }

      await RunPendingMigrationsAsync(targetIdentifier, cancellationToken);
   }

   /// <inheritdoc />
   public async Task RunAsync(IDbMigration migration, CancellationToken cancellationToken = default)
   {
      try
      {
         await _connection.InTransactionAsync(async () =>
         {
            await migration.UpAsync(_connection);

            await _connection.Dapper.ExecuteAsync(
               $"INSERT INTO {MIGRATIONS_TABLE_FULLY_QUALIFIED} (identifier, name, executed_at) VALUES (:identifier, :name, :executedAtUtc)",
               new Dictionary<string, object?> {
                     { "identifier", migration.Identifier },
                     { "name", migration.Name },
                     { "executedAtUtc", DateTime.UtcNow }
               },
               ct: cancellationToken
            );
         }
         );
      }
      catch (Exception exception)
      {
         throw new MigrationException(migration, exception);
      }
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
   ///    Determines whether an embedded schema should be applied.
   ///    Returns true if:
   ///    - Assemblies are configured for schema discovery
   ///    - An embedded schema resource exists in at least one assembly
   ///    - The database is empty
   ///    - If targetIdentifier is specified, every discovered schema's version must be &lt;= targetIdentifier.
   ///      A schema whose header version exceeds the target means the caller is asking for a state older
   ///      than one of the baselines can represent; applying only a subset would leave gaps that later
   ///      migrations cannot fill, so the entire schema-first bootstrap is skipped instead.
   /// </summary>
   private async Task<bool> ShouldApplySchemaAsync(long? targetIdentifier, CancellationToken cancellationToken)
   {
      if (_assemblies.Length == 0)
         return false;

      var contents = await EmbeddedSchemaDiscovery.ReadAllSchemaContentsAsync(_assemblies, _environment, cancellationToken);

      if (contents.Count == 0)
         return false;

      if (!await IsDatabaseEmptyAsync(cancellationToken))
         return false;

      if (targetIdentifier.HasValue)
      {
         foreach (var (content, _, _) in contents)
         {
            var info = SchemaFileParser.ParseMigrationVersion(content);

            if (info is not null && info.Value.Identifier > targetIdentifier.Value)
               return false;
         }
      }

      return true;
   }

   /// <summary>
   ///    Applies all embedded schema resources to the database in assembly order.
   ///    Each assembly contributes at most one schema file. The baseline migration row
   ///    is recorded using the highest identifier across all applied schema headers.
   /// </summary>
   private async Task ApplySchemaAsync(CancellationToken cancellationToken)
   {
      var schemas = await EmbeddedSchemaDiscovery.ReadAllSchemaContentsAsync(_assemblies, _environment, cancellationToken);

      if (schemas.Count == 0)
         throw new InvalidOperationException("No embedded schema resource found.");

      await _connection.InTransactionAsync(async () =>
      {
         // Pre-create the migrations table so schema files that also try to create it
         // (with or without IF NOT EXISTS) don't conflict within the same transaction.
         await EnsureMigrationTableExistsAsync();

         SchemaFileMigrationInfo? highestMigrationInfo = null;

         foreach (var (content, _, _) in schemas)
         {
            if (string.IsNullOrEmpty(content))
               continue;

            await _connection.Dapper.ExecuteAsync(content, ct: cancellationToken);

            var info = SchemaFileParser.ParseMigrationVersion(content);

            if (info is not null)
            {
               if (highestMigrationInfo is null || info.Value.Identifier > highestMigrationInfo.Value.Identifier)
                  highestMigrationInfo = info;
            }
         }

         // Record the highest migration version across all applied schemas as the baseline.
         if (highestMigrationInfo is not null)
         {
            await _connection.Dapper.ExecuteAsync(
               $"INSERT INTO {MIGRATIONS_TABLE_FULLY_QUALIFIED} (identifier, name, executed_at) VALUES (:identifier, :name, :executedAtUtc)",
               new Dictionary<string, object?> {
                  { "identifier", highestMigrationInfo.Value.Identifier },
                  { "name", highestMigrationInfo.Value.Name },
                  { "executedAtUtc", DateTime.UtcNow }
               },
               ct: cancellationToken
            );
         }
      });
   }

   /// <summary>
   ///    Runs all pending migrations, optionally up to a target identifier.
   /// </summary>
   private async Task RunPendingMigrationsAsync(long? targetIdentifier, CancellationToken cancellationToken)
   {
      var alreadyExecutedMigrations = (await RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
      var highestExecutedIdentifier = alreadyExecutedMigrations
         .Select(x => (long?)x.Identifier)
         .Max();

      var migrations = _migrationRetriever.RetrieveMigrations().AsEnumerable();

      if (targetIdentifier.HasValue)
         migrations = migrations.Where(x => x.Identifier <= targetIdentifier.Value);

      if (highestExecutedIdentifier.HasValue)
         migrations = migrations.Where(x => x.Identifier > highestExecutedIdentifier.Value);

      var orderedMigrations = migrations.OrderBy(x => x.Identifier).ToArray();

      foreach (var migration in orderedMigrations)
      {
         try
         {
            await RunAsync(migration, cancellationToken);
         }
         catch (Exception e)
         {
            throw new MigrationException(migration, e);
         }
      }
   }

   private async Task EnsureMigrationTableExistsAsync()
   {
      if (await _connection.Management.TableExistsAsync(MIGRATIONS_SCHEMA, MIGRATIONS_TABLE))
         return;

      await _connection.Dapper.ExecuteAsync($"CREATE SCHEMA IF NOT EXISTS \"{MIGRATIONS_SCHEMA}\";");

      await _connection.Dapper.ExecuteAsync(
         $"""
         CREATE TABLE IF NOT EXISTS {MIGRATIONS_TABLE_FULLY_QUALIFIED} (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );
         """
      );
   }
}
