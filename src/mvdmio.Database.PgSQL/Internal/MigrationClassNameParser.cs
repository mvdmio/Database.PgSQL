using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Internal;

/// <summary>
///    Parses the <c>Identifier</c> and <c>Name</c> of a migration from its class name.
///    The expected class name format is <c>_{identifier}_{name}</c>, e.g. <c>_202310191050_AddUsersTable</c>.
/// </summary>
internal static partial class MigrationClassNameParser
{
   // Matches: optional leading underscore, 12-digit timestamp, underscore, remaining name
   // E.g. "_202310191050_AddUsersTable" -> groups: ("202310191050", "AddUsersTable")
   [GeneratedRegex(@"^_?(\d{12})_(.+)$")]
   private static partial Regex MigrationNameRegex();

   /// <summary>
   ///    Extracts the numeric identifier from a migration class name.
   /// </summary>
   /// <param name="className">The unqualified class name, e.g. <c>_202310191050_AddUsersTable</c>.</param>
   /// <returns>The parsed <see cref="long"/> identifier.</returns>
   /// <exception cref="FormatException">
   ///    Thrown when <paramref name="className"/> does not match the expected
   ///    <c>_{identifier}_{name}</c> pattern.
   /// </exception>
   public static long ParseIdentifier(string className)
   {
      var match = MigrationNameRegex().Match(className);

      if (!match.Success)
         throw new FormatException($"Migration class name '{className}' does not match the expected format '_{'{'}identifier{'}'}_{'{'}name{'}'}' (e.g. '_202310191050_AddUsersTable').");

      return long.Parse(match.Groups[1].Value);
   }

   /// <summary>
   ///    Extracts the human-readable name from a migration class name.
   /// </summary>
   /// <param name="className">The unqualified class name, e.g. <c>_202310191050_AddUsersTable</c>.</param>
   /// <returns>The name portion of the class name, e.g. <c>AddUsersTable</c>.</returns>
   /// <exception cref="FormatException">
   ///    Thrown when <paramref name="className"/> does not match the expected
   ///    <c>_{identifier}_{name}</c> pattern.
   /// </exception>
   public static string ParseName(string className)
   {
      var match = MigrationNameRegex().Match(className);

      if (!match.Success)
         throw new FormatException($"Migration class name '{className}' does not match the expected format '_{'{'}identifier{'}'}_{'{'}name{'}'}' (e.g. '_202310191050_AddUsersTable').");

      return match.Groups[2].Value;
   }

   /// <summary>
   ///    Determines whether a class name matches the expected migration name format.
   /// </summary>
   /// <param name="className">The unqualified class name to check.</param>
   /// <returns><c>true</c> if the name is valid; otherwise <c>false</c>.</returns>
   public static bool IsValidClassName(string className) => MigrationNameRegex().IsMatch(className);
}
