namespace mvdmio.Database.PgSQL.Migrations.Interfaces;

/// <summary>
///    Interface for implementing database migrations.
/// </summary>
public interface IDbMigration
{
   /// <summary>
   ///    Identifier of the migration. Used to determine if this migration has already been done on the database.
   /// </summary>
   /// <example>
   ///    The best identifier is the current timestamp. E.g. 202310191050 for 2023-10-19 10:50
   /// </example>
   long Identifier { get; }

   /// <summary>
   ///    Human-readable name for this migration.
   /// </summary>
   string Name { get; }

   /// <summary>
   ///    Method for executing the migration on the database.
   /// </summary>
   /// <param name="db">The db-connection to execute the migration on.</param>
   Task UpAsync(DatabaseConnection db);
}
