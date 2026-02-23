using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture.Migrations;

public class _202505181000_SimpleTable : IDbMigration
{
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE TABLE public.simple_table (
             id                    BIGINT NOT NULL,
             required_string_value TEXT   NOT NULL,
             optional_string_value TEXT   NULL,
             PRIMARY KEY (id)
         )
         """
      );
   }
}
