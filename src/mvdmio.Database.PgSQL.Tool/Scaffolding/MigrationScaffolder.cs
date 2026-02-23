namespace mvdmio.Database.PgSQL.Tool.Scaffolding;

/// <summary>
///    Generates migration class file content.
/// </summary>
internal static class MigrationScaffolder
{
   /// <summary>
   ///    Generates the content of a new migration .cs file.
   /// </summary>
   /// <param name="migrationNamespace">The namespace for the migration class.</param>
   /// <param name="identifier">The timestamp-based identifier (e.g. 202602161430).</param>
   /// <param name="name">The human-readable migration name (e.g. AddUsersTable).</param>
   /// <returns>The full .cs file content as a string.</returns>
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

   /// <summary>
   ///    Generates a timestamp identifier from the current UTC time.
   /// </summary>
   /// <returns>A long in YYYYMMDDHHmm format.</returns>
   public static long GenerateIdentifier()
   {
      return long.Parse(DateTime.UtcNow.ToString("yyyyMMddHHmm"));
   }

   /// <summary>
   ///    Generates the file name for a migration.
   /// </summary>
   /// <param name="identifier">The timestamp-based identifier.</param>
   /// <param name="name">The human-readable migration name.</param>
   /// <returns>The file name (e.g. _202602161430_AddUsersTable.cs).</returns>
   public static string GenerateFileName(long identifier, string name)
   {
      return $"_{identifier}_{name}.cs";
   }
}
