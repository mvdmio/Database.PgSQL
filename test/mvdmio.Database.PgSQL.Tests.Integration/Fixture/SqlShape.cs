using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

/// <summary>
///    The shapes a rendered statement is matched against, as regular expressions: what a test claims about the SQL
///    should survive the table aliases the query provider chose and whether or not it quoted an identifier.
/// </summary>
internal static class SqlShape
{
   /// <summary>An equality between two qualified columns.</summary>
   public static string CrossTableEquality(string foreignKeyColumn, string keyColumn)
   {
      return $@"{QualifiedColumn(foreignKeyColumn)}\s*=\s*{QualifiedColumn(keyColumn)}";
   }

   /// <summary>One column, qualified by whatever alias its table was given.</summary>
   public static string QualifiedColumn(string columnName)
   {
      return $@"(?:""[^""]+""|\w+)\.""?{Regex.Escape(columnName)}""?";
   }
}
