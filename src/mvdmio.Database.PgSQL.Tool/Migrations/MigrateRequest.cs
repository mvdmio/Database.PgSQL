using mvdmio.Database.PgSQL.Migrations;

namespace mvdmio.Database.PgSQL.Tool.Migrations;

internal sealed record MigrateRequest(long? TargetIdentifier, DeployPhase Phase)
{
   public bool IsLatest => !TargetIdentifier.HasValue;

   public static MigrateRequest Latest(DeployPhase phase)
   {
      return new MigrateRequest(null, phase);
   }

   public static MigrateRequest To(long targetIdentifier, DeployPhase phase)
   {
      return new MigrateRequest(targetIdentifier, phase);
   }
}
