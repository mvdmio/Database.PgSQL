using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture.Migrations;

public class _202505192230_ComplexTable : IDbMigration
{
   public long Identifier { get; } = 202505192230;
   public string Name { get; } = "ComplexTable";
   
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE TABLE public.complex_table (
             first_id  BIGINT NOT NULL,
             second_id BIGINT NOT NULL,
             PRIMARY KEY (first_id, second_id)
         )
         """
      );
   }
}