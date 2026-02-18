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
   private readonly DatabaseConnection _connection;
   private readonly IMigrationRetriever _migrationRetriever;
   private readonly MigrationTableConfiguration _tableConfig;
   private readonly string? _schemaFilePath;

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class using reflection-based migration retrieval
   ///    with the default migration table configuration.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, params Assembly[] assembliesContainingMigrations)
      : this(connection, MigrationTableConfiguration.Default, schemaFilePath: null, assembliesContainingMigrations)
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
   ///    classes.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, MigrationTableConfiguration tableConfig, params Assembly[] assembliesContainingMigrations)
      : this(connection, tableConfig, schemaFilePath: null, assembliesContainingMigrations)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class using reflection-based migration retrieval
   ///    with a custom migration table configuration and an optional schema file for empty databases.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="tableConfig">The configuration for the migration tracking table.</param>
   /// <param name="schemaFilePath">
   ///    Optional path to a schema SQL file. If provided and the database is empty,
   ///    the schema file is applied before running migrations.
   /// </param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, MigrationTableConfiguration tableConfig, string? schemaFilePath, params Assembly[] assembliesContainingMigrations)
      : this(connection, tableConfig, schemaFilePath, new ReflectionMigrationRetriever(assembliesContainingMigrations))
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class with a custom migration retriever
   ///    and the default migration table configuration.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(DatabaseConnection connection, IMigrationRetriever migrationRetriever)
      : this(connection, MigrationTableConfiguration.Default, schemaFilePath: null, migrationRetriever)
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
      : this(connection, tableConfig, schemaFilePath: null, migrationRetriever)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class with a custom migration retriever,
   ///    a custom migration table configuration, and an optional schema file for empty databases.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="tableConfig">The configuration for the migration tracking table.</param>
   /// <param name="schemaFilePath">
   ///    Optional path to a schema SQL file. If provided and the database is empty,
   ///    the schema file is applied before running migrations.
   /// </param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(DatabaseConnection connection, MigrationTableConfiguration tableConfig, string? schemaFilePath, IMigrationRetriever migrationRetriever)
   {
      _connection = connection;
      _tableConfig = tableConfig;
      _schemaFilePath = schemaFilePath;
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
      // Check if database is empty and we have a schema file to apply
      if (await ShouldApplySchemaFileAsync(targetIdentifier: null, cancellationToken))
      {
         await ApplySchemaFileAsync(cancellationToken);
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
      // Check if database is empty and we have a schema file to apply
      if (await ShouldApplySchemaFileAsync(targetIdentifier, cancellationToken))
      {
         await ApplySchemaFileAsync(cancellationToken);
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
   ///    Determines whether a schema file should be applied.
   ///    Returns true if:
   ///    - A schema file path is configured
   ///    - The schema file exists
   ///    - The database is empty
   ///    - If targetIdentifier is specified, the schema version must be &lt;= targetIdentifier
   /// </summary>
   private async Task<bool> ShouldApplySchemaFileAsync(long? targetIdentifier, CancellationToken cancellationToken)
   {
      if (string.IsNullOrEmpty(_schemaFilePath))
         return false;

      if (!File.Exists(_schemaFilePath))
         return false;

      if (!await IsDatabaseEmptyAsync(cancellationToken))
         return false;

      // If we have a target identifier, check that the schema version is <= target
      if (targetIdentifier.HasValue)
      {
         var migrationInfo = await SchemaFileParser.ParseMigrationVersionFromFileAsync(_schemaFilePath, cancellationToken);

         if (migrationInfo is not null && migrationInfo.Value.Identifier > targetIdentifier.Value)
            return false;
      }

      return true;
   }

   /// <summary>
   ///    Applies the configured schema file to the database.
   /// </summary>
   private async Task ApplySchemaFileAsync(CancellationToken cancellationToken)
   {
      if (string.IsNullOrEmpty(_schemaFilePath))
         throw new InvalidOperationException("No schema file path configured.");

      if (!File.Exists(_schemaFilePath))
         throw new FileNotFoundException($"Schema file not found: {_schemaFilePath}", _schemaFilePath);

      // Parse the schema file to extract migration version
      var migrationInfo = await SchemaFileParser.ParseMigrationVersionFromFileAsync(_schemaFilePath, cancellationToken);

      // Read and execute the schema file
      var schemaContent = await File.ReadAllTextAsync(_schemaFilePath, cancellationToken);

      await _connection.InTransactionAsync(async () =>
      {
         await _connection.Dapper.ExecuteAsync(schemaContent, ct: cancellationToken);

         // Ensure migration table exists (it should have been created by the schema, but ensure it's there)
         await EnsureMigrationTableExistsAsync();

         // Record the migration version from the schema file
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
