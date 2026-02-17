using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Exceptions;

/// <summary>
///   Exception thrown when a migration fails.
/// </summary>
public class MigrationException : DatabaseException
{
   /// <summary>
   ///   The migration that caused the exception.
   /// </summary>
   public IDbMigration Migration { get; }

   /// <inheritdoc />
   public MigrationException(IDbMigration migration)
      : base($"Error while executing migration {migration.Identifier}: {migration.Name}.")
   {
      Migration = migration;
   }

   /// <inheritdoc />
   public MigrationException(IDbMigration migration, Exception inner)
      : base($"Error while executing migration {migration.Identifier}: {migration.Name}.", inner)
   {
      Migration = migration;
   }
}
