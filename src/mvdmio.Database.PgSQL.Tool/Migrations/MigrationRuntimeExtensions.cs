using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Tool.Migrations;

internal static class MigrationRuntimeExtensions
{
   /// <summary>
   ///    Reads the rows recorded in the migrations table as they are, without backfilling scopes. A database with
   ///    no migrations table, or an empty one, has no rows.
   /// </summary>
   public static async Task<ExecutedMigrationModel[]> RetrieveRecordedMigrationsAsync(this IMigrationRuntime runtime, CancellationToken cancellationToken)
   {
      if (await runtime.IsDatabaseEmptyAsync(cancellationToken))
         return [];

      return (await runtime.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
   }
}
