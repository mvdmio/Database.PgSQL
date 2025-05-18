using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture.Migrations;

public class _202505181000_TestTable : IDbMigration
{
   public long Identifier { get; } = 202505181000;
   public string Name { get; } = "TestTable";
   
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE TABLE public.test_table (
             id                    BIGINT NOT NULL,
             required_string_value TEXT   NOT NULL,
             optional_string_value TEXT   NULL,
             PRIMARY KEY (id)
         )
         """
      );
   }
}