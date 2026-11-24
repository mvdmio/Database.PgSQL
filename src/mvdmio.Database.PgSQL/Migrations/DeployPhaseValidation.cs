namespace mvdmio.Database.PgSQL.Migrations;

internal static class DeployPhaseValidation
{
   public static void ThrowIfInvalid(DeployPhase phase)
   {
      if (phase == 0 || (phase & ~DeployPhase.All) != 0)
         throw new ArgumentOutOfRangeException(nameof(phase), phase, $"The deploy phase must be {nameof(DeployPhase.PreDeploy)}, {nameof(DeployPhase.PostDeploy)} or {nameof(DeployPhase.All)}.");
   }
}
