using System.Reflection;
using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Internal;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Class for running database migrations.
/// </summary>
[PublicAPI]
public class DatabaseMigrator : IDatabaseMigrator
{
   private readonly DatabaseConnection _connection;
   private readonly IMigrationRetriever _migrationRetriever;

   /// <summary>
   ///    Constructor.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="assembliesContainingMigrations">
   ///    List of assemblies to use for searching <see cref="IDbMigration" />
   ///    classes.
   /// </param>
   public DatabaseMigrator(DatabaseConnection connection, params Assembly[] assembliesContainingMigrations)
      : this(connection, new ReflectionMigrationRetriever(assembliesContainingMigrations))
   {
   }

   /// <summary>
   ///    Constructor.
   /// </summary>
   /// <param name="connection">The database connection to use for migrations.</param>
   /// <param name="migrationRetriever">The migration retriever to use.</param>
   public DatabaseMigrator(DatabaseConnection connection, IMigrationRetriever migrationRetriever)
   {
      _connection = connection;
      _migrationRetriever = migrationRetriever;
   }

   /// <inheritdoc />
   public IEnumerable<ExecutedMigrationModel> RetrieveAlreadyExecutedMigrations()
   {
      return AsyncHelper.RunSync(() => RetrieveAlreadyExecutedMigrationsAsync());
   }

   /// <inheritdoc />
   public async Task<IEnumerable<ExecutedMigrationModel>> RetrieveAlreadyExecutedMigrationsAsync(CancellationToken cancellationToken = default)
   {
      return await _connection.Dapper.QueryAsync<ExecutedMigrationModel>(
         """
         SELECT
            identifier AS identifier,
            name AS name,
            executed_at AS executedAtUtc
         FROM mvdmio.migrations
         """
      );
   }

   /// <inheritdoc />
   public void MigrateDatabaseToLatest()
   {
      AsyncHelper.RunSync(() => MigrateDatabaseToLatestAsync());
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
   public void Run(IDbMigration migration)
   {
      AsyncHelper.RunSync(() => RunAsync(migration));
   }

   /// <inheritdoc />
   public async Task RunAsync(IDbMigration migration, CancellationToken cancellationToken = default)
   {
      try
      {
         await _connection.InTransactionAsync(async () => {
               await migration.UpAsync(_connection);

               await _connection.Dapper.ExecuteAsync(
                  "INSERT INTO mvdmio.migrations (identifier, name, executed_at) VALUES (:identifier, :name, :executedAtUtc)",
                  new Dictionary<string, object?> {
                     { "identifier", migration.Identifier },
                     { "name", migration.Name },
                     { "executedAtUtc", DateTime.UtcNow }
                  }
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
      if (await _connection.Management.TableExistsAsync("mvdmio", "migrations"))
         return;

      await _connection.Dapper.ExecuteAsync("CREATE SCHEMA IF NOT EXISTS mvdmio;");

      await _connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS mvdmio.migrations (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );
         """
      );
   }
}