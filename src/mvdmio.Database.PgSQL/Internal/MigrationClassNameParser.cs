using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Internal;

internal static partial class MigrationClassNameParser
{
   [GeneratedRegex(@"^_?(\d{12})_(.+)$")]
   private static partial Regex MigrationNameRegex();

   public static long ParseIdentifier(string className)
   {
      var match = MigrationNameRegex().Match(className);

      if (!match.Success)
         throw new FormatException($"Migration class name '{className}' does not match the expected format '_{'{'}identifier{'}'}_{'{'}name{'}'}' (e.g. '_202310191050_AddUsersTable').");

      return long.Parse(match.Groups[1].Value);
   }

   public static string ParseName(string className)
   {
      var match = MigrationNameRegex().Match(className);

      if (!match.Success)
         throw new FormatException($"Migration class name '{className}' does not match the expected format '_{'{'}identifier{'}'}_{'{'}name{'}'}' (e.g. '_202310191050_AddUsersTable').");

      return match.Groups[2].Value;
   }

   public static bool IsValidClassName(string className) => MigrationNameRegex().IsMatch(className);
}
