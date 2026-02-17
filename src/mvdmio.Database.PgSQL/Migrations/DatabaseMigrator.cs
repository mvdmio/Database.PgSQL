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
      : this(connection, MigrationTableConfiguration.Default, assembliesContainingMigrations)
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
      : this(connection, tableConfig, new ReflectionMigrationRetriever(assembliesContainingMigrations))
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="DatabaseMigrator"/> class with a custom migration retriever
   ///    and the default migration table configuration.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(DatabaseConnection connection, IMigrationRetriever migrationRetriever)
      : this(connection, MigrationTableConfiguration.Default, migrationRetriever)
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
   {
      _connection = connection;
      _tableConfig = tableConfig;
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
      await EnsureMigrationTableExistsAsync();

      var alreadyExecutedMigrations = (await RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
      var orderedMigrations = _migrationRetriever.RetrieveMigrations().OrderBy(x => x.Identifier).ToArray();

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

   /// <inheritdoc />
   public async Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken = default)
   {
      await EnsureMigrationTableExistsAsync();

      var alreadyExecutedMigrations = (await RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
      var orderedMigrations = _migrationRetriever.RetrieveMigrations()
         .Where(x => x.Identifier <= targetIdentifier)
         .OrderBy(x => x.Identifier)
         .ToArray();

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
