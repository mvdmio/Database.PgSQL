using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Tool.Migrations;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

internal static class PhaseOption
{
   public static Option<DeployPhase> Create()
   {
      var option = new Option<DeployPhase>("--phase")
      {
         Description = $"The deploy phase to run: {string.Join(", ", DeployPhaseNames.Values)} (default: {DeployPhaseNames.PRE_DEPLOY})",
         DefaultValueFactory = _ => DeployPhase.PreDeploy,
         CustomParser = result =>
         {
            var text = result.Tokens.Count == 1 ? result.Tokens[0].Value : null;
            if (DeployPhaseNames.TryParse(text, out var phase))
               return phase;

            result.AddError($"Argument '{text}' not recognized for option '--phase'. Must be one of: {string.Join(", ", DeployPhaseNames.Values)}.");
            return default;
         }
      };

      option.CompletionSources.Add(DeployPhaseNames.Values.ToArray());
      return option;
   }
}
