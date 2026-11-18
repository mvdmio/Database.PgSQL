using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

internal static class SqlShape
{
   public static string CrossTableEquality(string foreignKeyColumn, string keyColumn)
   {
      return $@"{QualifiedColumn(foreignKeyColumn)}\s*=\s*{QualifiedColumn(keyColumn)}";
   }

   public static string QualifiedColumn(string columnName)
   {
      return $@"(?:""[^""]+""|\w+)\.""?{Regex.Escape(columnName)}""?";
   }
}
