using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema.Migrations;

/// <summary>
///    Mirrors the baseline recorded by this assembly's embedded schema.sql: same identifier, same table.
///    After a schema-first bootstrap this migration must not run (the baseline row covers it); without the
///    schema it creates the table itself.
/// </summary>
public class _202505181100_SecondaryTable : IDbMigration
{
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE TABLE public.secondary_table (
             id                    BIGINT NOT NULL,
             description           TEXT   NOT NULL,
             PRIMARY KEY (id)
         )
         """
      );
   }
}
