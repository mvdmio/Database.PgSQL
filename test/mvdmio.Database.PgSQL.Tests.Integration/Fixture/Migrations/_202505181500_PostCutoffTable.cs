using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture.Migrations;

public class _202505181500_PostCutoffTable : IDbMigration
{
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE TABLE public.post_cutoff_table (
             id BIGINT NOT NULL,
             PRIMARY KEY (id)
         )
         """
      );
   }
}
