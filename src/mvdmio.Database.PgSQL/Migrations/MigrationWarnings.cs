using Microsoft.Extensions.Logging;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Migrations;

internal static class MigrationWarnings
{
   public static void LogOutOfOrder(ILogger logger, PendingMigration pending)
   {
      logger.LogWarning(
         "Running out-of-order migration {Identifier} ({Name}) in scope {Scope}: it is below the scope's watermark {Watermark}, so it runs later than its identifier suggests.",
         pending.Migration.Identifier,
         pending.Migration.Name,
         pending.Migration.Scope,
         pending.ScopeWatermark
      );
   }

   public static void LogWaitingPostDeployParts(ILogger logger, IEnumerable<IDbMigration> migrations)
   {
      foreach (var migration in migrations)
      {
         logger.LogWarning(
            "The post-deploy part of migration {Identifier} ({Name}) in scope {Scope} is waiting from an earlier run: its pre-deploy part has run, and only a post-deploy run runs it.",
            migration.Identifier,
            migration.Name,
            migration.Scope
         );
      }
   }

   public static void LogBlockedPostDeployParts(ILogger logger, IEnumerable<IDbMigration> migrations)
   {
      foreach (var migration in migrations)
      {
         logger.LogWarning(
            "The post-deploy part of migration {Identifier} ({Name}) in scope {Scope} does not run: its pre-deploy part is still pending, so run a pre-deploy phase first.",
            migration.Identifier,
            migration.Name,
            migration.Scope
         );
      }
   }
}
