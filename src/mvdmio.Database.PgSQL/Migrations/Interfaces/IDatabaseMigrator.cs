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
   ///    Run all migrations that have not yet been executed in order. A migration is pending when its scope has no
   ///    row for it and either the scope has no rows or the migration's identifier is above the scope's baseline
   ///    (the lowest identifier recorded for that scope). Everything at or below the baseline counts as present, so
   ///    migrations folded into a bootstrap schema never run. A pending migration below the scope's watermark (the
   ///    highest recorded identifier) is out-of-order: it still runs, preceded by a logged warning.
   ///    Schema-first bootstrap is per scope: an assembly's embedded schema (based on the configured environment)
   ///    is applied when at least one scope that assembly vouches for has no rows yet. Assemblies whose
   ///    vouched scopes already have rows are left alone. After baselines are recorded, the remaining pending
   ///    migrations are run in identifier order.
   ///    Cancellation lets a migration, or an embedded-schema bootstrap, that has started finish. The run then starts
   ///    nothing further and throws <see cref="OperationCanceledException" />, never a
   ///    <see cref="Exceptions.MigrationException" />. Cancelling while waiting for the migration lock stops at once.
   /// </summary>
   Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken = default);

   /// <summary>
   ///    Run all pending migrations up to and including the specified identifier. The target is a global ceiling
   ///    applied per scope: every scope advances up to the given identifier, out-of-order migrations included.
   ///    Schema-first bootstrap is per scope: an assembly's embedded schema is applied when at least one scope
   ///    that assembly vouches for has no rows yet, unless that schema's header contains an identifier above
   ///    <paramref name="targetIdentifier"/> — that schema is skipped, while other assemblies are still considered.
   ///    After baselines are recorded, remaining migrations up to the target are run.
   ///    Cancellation lets a migration, or an embedded-schema bootstrap, that has started finish. The run then starts
   ///    nothing further and throws <see cref="OperationCanceledException" />, never a
   ///    <see cref="Exceptions.MigrationException" />. Cancelling while waiting for the migration lock stops at once.
   /// </summary>
   /// <param name="targetIdentifier">The migration identifier to migrate up to (inclusive).</param>
   /// <param name="cancellationToken">Cancellation token.</param>
   Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken = default);

   /// <summary>
   ///    Run the parts of all pending migrations that belong to the requested phase, in identifier order, with the
   ///    pre-deploy part of a migration before its post-deploy part. <see cref="MigrateDatabaseToLatestAsync(CancellationToken)" />
   ///    states which pre-deploy parts are pending; it equals this method with <see cref="DeployPhase.PreDeploy" />.
   ///    A post-deploy part is pending when its migration implements <see cref="IDbMigration.PostDeployAsync" />, has a
   ///    pre-deploy row, has no post-deploy row, and its identifier is above the scope's baseline. In a
   ///    <see cref="DeployPhase.PostDeploy" /> run the pre-deploy row must exist before the run starts; in a
   ///    <see cref="DeployPhase.All" /> run a post-deploy part runs right after the pre-deploy part of the same run.
   ///    A pre-deploy run logs a warning naming each post-deploy part left waiting from an earlier run, and a
   ///    post-deploy run logs a warning naming each migration whose post-deploy part is blocked by a pending
   ///    pre-deploy part.
   /// </summary>
   /// <param name="phase">The phase to run: <see cref="DeployPhase.PreDeploy" />, <see cref="DeployPhase.PostDeploy" /> or <see cref="DeployPhase.All" />.</param>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <exception cref="ArgumentOutOfRangeException">The phase is zero or has a bit outside <see cref="DeployPhase.All" />.</exception>
   Task MigrateDatabaseToLatestAsync(DeployPhase phase, CancellationToken cancellationToken = default);

   /// <summary>
   ///    Run the parts of all pending migrations up to and including the specified identifier that belong to the
   ///    requested phase. The identifier ceiling applies to both parts of a migration, and
   ///    <see cref="MigrateDatabaseToAsync(long, CancellationToken)" /> equals this method with
   ///    <see cref="DeployPhase.PreDeploy" />. <see cref="MigrateDatabaseToLatestAsync(DeployPhase, CancellationToken)" />
   ///    describes which parts are pending.
   /// </summary>
   /// <param name="targetIdentifier">The migration identifier to migrate up to (inclusive).</param>
   /// <param name="phase">The phase to run: <see cref="DeployPhase.PreDeploy" />, <see cref="DeployPhase.PostDeploy" /> or <see cref="DeployPhase.All" />.</param>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <exception cref="ArgumentOutOfRangeException">The phase is zero or has a bit outside <see cref="DeployPhase.All" />.</exception>
   Task MigrateDatabaseToAsync(long targetIdentifier, DeployPhase phase, CancellationToken cancellationToken = default);

   /// <summary>
   ///    Run the pre-deploy part of a migration on the database and record its history row.
   ///    A token cancelled before the migration starts throws <see cref="OperationCanceledException" /> and runs
   ///    nothing. A migration that has started finishes and records its row; cancellation never interrupts it.
   /// </summary>
   Task RunAsync(IDbMigration migration, CancellationToken cancellationToken = default);

   /// <summary>
   ///    Checks whether the migrations table is missing or has no rows at all. This is a global check: a row
   ///    belonging to any scope makes the database non-empty. Schema-first bootstrap checks each scope's rows
   ///    instead of this method.
   /// </summary>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <returns>True if no migrations have been applied (empty database), false otherwise.</returns>
   Task<bool> IsDatabaseEmptyAsync(CancellationToken cancellationToken = default);
}
