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
   ///    Run all migrations that have not yet been executed in order. A migration is pending when its identifier
   ///    is ahead of the highest executed identifier within its own scope.
   ///    Schema-first bootstrap is per scope: an assembly's embedded schema (based on the configured environment)
   ///    is applied when at least one scope that assembly vouches for has no watermark yet. Assemblies whose
   ///    vouched scopes already have rows are left alone. After baselines are recorded, remaining migrations past
   ///    each scope's watermark are run.
   /// </summary>
   Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken = default);

   /// <summary>
   ///    Run all pending migrations up to and including the specified identifier. The target is a global ceiling
   ///    applied per scope: every scope advances up to the given identifier.
   ///    Schema-first bootstrap is per scope: an assembly's embedded schema is applied when at least one scope
   ///    that assembly vouches for has no watermark yet, unless that schema's header contains an identifier above
   ///    <paramref name="targetIdentifier"/> — that schema is skipped, while other assemblies are still considered.
   ///    After baselines are recorded, remaining migrations up to the target are run.
   /// </summary>
   /// <param name="targetIdentifier">The migration identifier to migrate up to (inclusive).</param>
   /// <param name="cancellationToken">Cancellation token.</param>
   Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken = default);

   /// <summary>
   ///    Run a migration on the database. Returns true if the migration ran successfully. False otherwise.
   /// </summary>
   Task RunAsync(IDbMigration migration, CancellationToken cancellationToken = default);

   /// <summary>
   ///    Checks whether the migrations table is missing or has no rows at all. This is a global check: a row
   ///    belonging to any scope makes the database non-empty. Schema-first bootstrap uses per-scope watermarks
   ///    instead of this method.
   /// </summary>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <returns>True if no migrations have been applied (empty database), false otherwise.</returns>
   Task<bool> IsDatabaseEmptyAsync(CancellationToken cancellationToken = default);
}
