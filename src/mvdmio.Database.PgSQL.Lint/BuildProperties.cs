using Microsoft.CodeAnalysis.Diagnostics;

namespace mvdmio.Database.PgSQL.Lint;

internal static class BuildProperties
{
   public static bool IsTrue(AnalyzerOptions options, string property)
   {
      return options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property." + property, out var value)
             && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
   }
}
