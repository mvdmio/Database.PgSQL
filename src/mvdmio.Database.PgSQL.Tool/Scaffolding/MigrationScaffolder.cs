namespace mvdmio.Database.PgSQL.Tool.Scaffolding;

internal static class MigrationScaffolder
{
   public static string GenerateContent(string migrationNamespace, long identifier, string name)
   {
      return $$""""
               using mvdmio.Database.PgSQL;
               using mvdmio.Database.PgSQL.Migrations.Interfaces;

               namespace {{migrationNamespace}};

               public class _{{identifier}}_{{name}} : IDbMigration
               {
                  public async Task UpAsync(DatabaseConnection db)
                  {
                     await db.Dapper.ExecuteAsync(
                        """
                        -- TODO: Write your migration SQL here
                        """
                     );
                  }
               }

               """";
   }

   public static long GenerateIdentifier()
   {
      return long.Parse(DateTime.UtcNow.ToString("yyyyMMddHHmm"));
   }

   public static string GenerateFileName(long identifier, string name)
   {
      return $"_{identifier}_{name}.cs";
   }
}
