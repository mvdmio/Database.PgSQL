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
   Task<IEnumerable<ExecutedMigrationModel>> RetrieveAlreadyExecutedMigrationsAsync(CancellationToken cancellationToken);

   /// <summary>
   ///    Run all migrations that have not yet been executed in order.
   /// </summary>
   Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken);

   /// <summary>
   ///    Run a migration on the database. Returns true if the migration ran successfully. False otherwise.
   /// </summary>
   Task RunAsync(IDbMigration migration, CancellationToken cancellationToken);
}