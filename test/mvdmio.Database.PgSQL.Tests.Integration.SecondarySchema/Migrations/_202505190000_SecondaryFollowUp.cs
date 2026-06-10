using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema.Migrations;

/// <summary>
///    A migration past this assembly's schema baseline (202505181100), so schema-first multi-scope tests can
///    verify that each scope's post-baseline migrations run.
/// </summary>
public class _202505190000_SecondaryFollowUp : IDbMigration
{
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE TABLE public.secondary_follow_up_table (
             id BIGINT NOT NULL,
             PRIMARY KEY (id)
         )
         """
      );
   }
}
