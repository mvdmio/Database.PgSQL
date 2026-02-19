using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Migrations.Interfaces;

/// <summary>
///   Interface for a database migrator.
/// </summary>
public interface IDatabaseMigrator
{
   /// <summary>
   ///    Retrieve all migrations that have already been executed.
   /// </summary>
   Task<IEnumerable<ExecutedMigrationModel>> RetrieveAlreadyExecutedMigrationsAsync(CancellationToken cancellationToken = default);

   /// <summary>
   ///    Run all migrations that have not yet been executed in order.
   ///    If the database is empty and an embedded schema resource is found (based on the configured environment),
   ///    the schema is applied first, then any remaining migrations after the schema version are run.
   /// </summary>
   Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken = default);

   /// <summary>
   ///    Run all pending migrations up to and including the specified identifier.
   ///    If the database is empty and an embedded schema resource is found (with a version &lt;= targetIdentifier),
   ///    the schema is applied first, then any remaining migrations up to the target are run.
   /// </summary>
   /// <param name="targetIdentifier">The migration identifier to migrate up to (inclusive).</param>
   /// <param name="cancellationToken">Cancellation token.</param>
   Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken = default);

   /// <summary>
   ///    Run a migration on the database. Returns true if the migration ran successfully. False otherwise.
   /// </summary>
   Task RunAsync(IDbMigration migration, CancellationToken cancellationToken = default);

   /// <summary>
   ///    Checks whether the database has any migrations applied.
   /// </summary>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <returns>True if no migrations have been applied (empty database), false otherwise.</returns>
   Task<bool> IsDatabaseEmptyAsync(CancellationToken cancellationToken = default);
}
