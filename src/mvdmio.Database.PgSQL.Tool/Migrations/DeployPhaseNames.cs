using mvdmio.Database.PgSQL.Migrations;

namespace mvdmio.Database.PgSQL.Tool.Migrations;

internal static class DeployPhaseNames
{
   public const string PRE_DEPLOY = "pre-deploy";
   public const string POST_DEPLOY = "post-deploy";
   public const string ALL = "all";

   public static IReadOnlyList<string> Values { get; } = [PRE_DEPLOY, POST_DEPLOY, ALL];

   public static bool TryParse(string? text, out DeployPhase phase)
   {
      switch (text)
      {
         case PRE_DEPLOY:
            phase = DeployPhase.PreDeploy;
            return true;
         case POST_DEPLOY:
            phase = DeployPhase.PostDeploy;
            return true;
         case ALL:
            phase = DeployPhase.All;
            return true;
         default:
            phase = default;
            return false;
      }
   }

   public static string Format(DeployPhase phase)
   {
      return phase switch
      {
         DeployPhase.PreDeploy => PRE_DEPLOY,
         DeployPhase.PostDeploy => POST_DEPLOY,
         DeployPhase.All => ALL,
         _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown deploy phase.")
      };
   }
}
