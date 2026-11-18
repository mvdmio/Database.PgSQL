using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema.Migrations;

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
