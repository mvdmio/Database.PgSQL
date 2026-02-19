using System.Reflection;
using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Class for running database migrations.
/// </summary>
[PublicAPI]
public sealed class DatabaseMigrator : IDatabaseMigrator
{
   private readonly DatabaseConnection _connection;
   private readonly IMigrationRetriever _migrationRetriever;
   private readonly MigrationTableConfiguration _tableConfig;
   private readonly string? _environment;
   private readonly Assembly[] _assemblies;

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class using reflection-based migration retrieval
   ///    with the default migration table configuration.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes. These assemblies are also searched for embedded schema resources.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, params Assembly[] assembliesContainingMigrations)
      : this(connection, MigrationTableConfiguration.Default, environment: null, assembliesContainingMigrations)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class using reflection-based migration retrieval
   ///    with a custom migration table configuration.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="tableConfig">The configuration for the migration tracking table.</param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes. These assemblies are also searched for embedded schema resources.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, MigrationTableConfiguration tableConfig, params Assembly[] assembliesContainingMigrations)
      : this(connection, tableConfig, environment: null, assembliesContainingMigrations)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class using reflection-based migration retrieval
   ///    with a custom migration table configuration and environment-based schema discovery.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="tableConfig">The configuration for the migration tracking table.</param>
   /// <param name="environment">
   ///    Optional environment name for schema discovery. If specified, looks for an embedded
   ///    schema.{environment}.sql resource (case-insensitive). Falls back to schema.sql if not found.
   ///    If the database is empty, the embedded schema is applied before running migrations.
   /// </param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes. These assemblies are also searched for embedded schema resources.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, MigrationTableConfiguration tableConfig, string? environment, params Assembly[] assembliesContainingMigrations)
      : this(connection, tableConfig, environment, assembliesContainingMigrations, new ReflectionMigrationRetriever(assembliesContainingMigrations))
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class with a custom migration retriever
   ///    and the default migration table configuration.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(DatabaseConnection connection, IMigrationRetriever migrationRetriever)
      : this(connection, MigrationTableConfiguration.Default, environment: null, [], migrationRetriever)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class with a custom migration retriever
   ///    and a custom migration table configuration.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="tableConfig">The configuration for the migration tracking table.</param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(DatabaseConnection connection, MigrationTableConfiguration tableConfig, IMigrationRetriever migrationRetriever)
      : this(connection, tableConfig, environment: null, [], migrationRetriever)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class with a custom migration retriever,
   ///    a custom migration table configuration, and environment-based schema discovery.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="tableConfig">The configuration for the migration tracking table.</param>
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
      MigrationTableConfiguration tableConfig,
      string? environment,
      Assembly[] assembliesForSchemaDiscovery,
      IMigrationRetriever migrationRetriever)
   {
      _connection = connection;
      _tableConfig = tableConfig;
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
         FROM {_tableConfig.FullyQualifiedTableName}
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
               $"INSERT INTO {_tableConfig.FullyQualifiedTableName} (identifier, name, executed_at) VALUES (:identifier, :name, :executedAtUtc)",
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
      var tableExists = await _connection.Management.TableExistsAsync(_tableConfig.Schema, _tableConfig.Table);

      if (!tableExists)
         return true;

      var count = await _connection.Dapper.ExecuteScalarAsync<long>(
         $"SELECT COUNT(*) FROM {_tableConfig.FullyQualifiedTableName}",
         ct: cancellationToken
      );

      return count == 0;
   }

   /// <summary>
   ///    Determines whether an embedded schema should be applied.
   ///    Returns true if:
   ///    - Assemblies are configured for schema discovery
   ///    - An embedded schema resource exists
   ///    - The database is empty
   ///    - If targetIdentifier is specified, the schema version must be &lt;= targetIdentifier
   /// </summary>
   private async Task<bool> ShouldApplySchemaAsync(long? targetIdentifier, CancellationToken cancellationToken)
   {
      if (_assemblies.Length == 0)
         return false;

      if (!EmbeddedSchemaDiscovery.SchemaResourceExists(_assemblies, _environment))
         return false;

      if (!await IsDatabaseEmptyAsync(cancellationToken))
         return false;

      // If we have a target identifier, check that the schema version is <= target
      if (targetIdentifier.HasValue)
      {
         var schemaContent = await EmbeddedSchemaDiscovery.ReadSchemaContentAsync(_assemblies, _environment, cancellationToken);

         if (schemaContent is not null)
         {
            var migrationInfo = SchemaFileParser.ParseMigrationVersion(schemaContent);

            if (migrationInfo is not null && migrationInfo.Value.Identifier > targetIdentifier.Value)
               return false;
         }
      }

      return true;
   }

   /// <summary>
   ///    Applies the embedded schema resource to the database.
   /// </summary>
   private async Task ApplySchemaAsync(CancellationToken cancellationToken)
   {
      var schemaContent = await EmbeddedSchemaDiscovery.ReadSchemaContentAsync(_assemblies, _environment, cancellationToken);

      if (string.IsNullOrEmpty(schemaContent))
         throw new InvalidOperationException("No embedded schema resource found.");

      // Parse the schema content to extract migration version
      var migrationInfo = SchemaFileParser.ParseMigrationVersion(schemaContent);

      await _connection.InTransactionAsync(async () =>
      {
         await _connection.Dapper.ExecuteAsync(schemaContent, ct: cancellationToken);

         // Ensure migration table exists (it should have been created by the schema, but ensure it's there)
         await EnsureMigrationTableExistsAsync();

         // Record the migration version from the schema
         if (migrationInfo is not null)
         {
            await _connection.Dapper.ExecuteAsync(
               $"INSERT INTO {_tableConfig.FullyQualifiedTableName} (identifier, name, executed_at) VALUES (:identifier, :name, :executedAtUtc)",
               new Dictionary<string, object?> {
                  { "identifier", migrationInfo.Value.Identifier },
                  { "name", migrationInfo.Value.Name },
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

      var migrations = _migrationRetriever.RetrieveMigrations().AsEnumerable();

      if (targetIdentifier.HasValue)
         migrations = migrations.Where(x => x.Identifier <= targetIdentifier.Value);

      var orderedMigrations = migrations.OrderBy(x => x.Identifier).ToArray();

      foreach (var migration in orderedMigrations)
      {
         if (alreadyExecutedMigrations.Any(x => x.Identifier == migration.Identifier))
            continue;

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
      if (await _connection.Management.TableExistsAsync(_tableConfig.Schema, _tableConfig.Table))
         return;

      await _connection.Dapper.ExecuteAsync($"CREATE SCHEMA IF NOT EXISTS \"{_tableConfig.Schema}\";");

      await _connection.Dapper.ExecuteAsync(
         $"""
         CREATE TABLE IF NOT EXISTS {_tableConfig.FullyQualifiedTableName} (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );
         """
      );
   }
}
