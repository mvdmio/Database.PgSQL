using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;

/// <summary>
///    Interface for implementing migration retrievers.
/// </summary>
public interface IMigrationRetriever
{
   /// <summary>
   ///    Retrieve generic migrations.
   /// </summary>
   IEnumerable<IDbMigration> RetrieveMigrations();
}