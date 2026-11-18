using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Tool.Migrations;

internal static class MigrationRuntimeExtensions
{
   public static async Task<ExecutedMigrationModel[]> RetrieveRecordedMigrationsAsync(this IMigrationRuntime runtime, CancellationToken cancellationToken)
   {
      if (await runtime.IsDatabaseEmptyAsync(cancellationToken))
         return [];

      return (await runtime.RetrieveAlreadyExecutedMigrationsAsync(cancellationToken)).ToArray();
   }
}
