using mvdmio.Database.PgSQL.Internal;

namespace mvdmio.Database.PgSQL.Migrations.Interfaces;

/// <summary>
///    Interface for implementing database migrations.
/// </summary>
/// <remarks>
///    The <see cref="Identifier"/> and <see cref="Name"/> properties have default implementations that extract
///    their values from the class name. The expected class name format is <c>_{identifier}_{name}</c>,
///    e.g. <c>_202310191050_AddUsersTable</c>. A Roslyn analyzer warns when the class name does not follow
///    this convention.
/// </remarks>
public interface IDbMigration
{
   /// <summary>
   ///    Identifier of the migration. Used to determine if this migration has already been done on the database.
   ///    Defaults to the numeric timestamp extracted from the class name (e.g. <c>202310191050</c> from
   ///    <c>_202310191050_AddUsersTable</c>).
   /// </summary>
   long Identifier => MigrationClassNameParser.ParseIdentifier(GetType().Name);

   /// <summary>
   ///    Human-readable name for this migration.
   ///    Defaults to the name portion extracted from the class name (e.g. <c>AddUsersTable</c> from
   ///    <c>_202310191050_AddUsersTable</c>).
   /// </summary>
   string Name => MigrationClassNameParser.ParseName(GetType().Name);

   /// <summary>
   ///    Scope this migration belongs to. Migrations are tracked per scope: whether this migration runs depends only
   ///    on the rows recorded <i>within its own scope</i>, independent of other scopes (see
   ///    <see cref="IDatabaseMigrator.MigrateDatabaseToLatestAsync" /> for the rule). Defaults to the declaring
   ///    assembly's simple name. Override this to keep a stable scope across an assembly rename — renaming the
   ///    assembly without overriding the scope forks the migration history and re-runs every migration.
   /// </summary>
   string Scope => GetType().Assembly.GetName().Name ?? throw new InvalidOperationException($"Cannot determine default scope for migration '{GetType().FullName}': the declaring assembly has no simple name. Override {nameof(Scope)} explicitly.");

   /// <summary>
   ///    Whether the migrator runs this migration, together with its history row, in one transaction. Defaults to
   ///    <c>true</c>: a failing migration rolls back as a whole.
   ///    Override this to return <c>false</c> to run the migration with no transaction open, so each statement
   ///    commits as soon as it finishes and releases its locks then (needed for batched back-fills and for
   ///    <c>CREATE INDEX CONCURRENTLY</c>). The migrator then records the migration only after <see cref="UpAsync" />
   ///    returns without an error.
   /// </summary>
   /// <remarks>
   ///    A migration that runs outside a transaction must give the same result when run again from its first
   ///    statement: a failure partway leaves the earlier statements applied and the migration unrecorded, so the next
   ///    migrate runs it again from the start.
   /// </remarks>
   bool RunsInTransaction => true;

   /// <summary>
   ///    Method for executing the migration on the database.
   /// </summary>
   /// <param name="db">The db-connection to execute the migration on.</param>
   Task UpAsync(DatabaseConnection db);
}
