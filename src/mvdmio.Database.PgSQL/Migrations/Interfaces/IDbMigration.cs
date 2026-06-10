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
   ///    Scope this migration belongs to. Migrations are watermarked per scope: a migration runs when its
   ///    <see cref="Identifier" /> is ahead of the highest executed identifier <i>within its own scope</i>,
   ///    independent of other scopes. Defaults to the declaring assembly's simple name. Override this to keep
   ///    a stable scope across an assembly rename — renaming the assembly without overriding the scope forks
   ///    the migration history and re-runs every migration.
   /// </summary>
   string Scope => GetType().Assembly.GetName().Name ?? throw new InvalidOperationException($"Cannot determine default scope for migration '{GetType().FullName}': the declaring assembly has no simple name. Override {nameof(Scope)} explicitly.");

   /// <summary>
   ///    Method for executing the migration on the database.
   /// </summary>
   /// <param name="db">The db-connection to execute the migration on.</param>
   Task UpAsync(DatabaseConnection db);
}
