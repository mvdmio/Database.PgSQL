using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class TransactionRefusedStatements
{
   private static readonly Regex _sqlComments = new(@"--[^\r\n]*|/\*.*?(\*/|\z)", RegexOptions.Compiled | RegexOptions.Singleline);
   private static readonly Regex _wordSeparators = new(@"[\s(),]+", RegexOptions.Compiled);

   private static readonly StatementPattern[] _patterns =
   [
      new("CREATE INDEX CONCURRENTLY", ["CREATE", "INDEX", "CONCURRENTLY"]),
      new("CREATE INDEX CONCURRENTLY", ["CREATE", "UNIQUE", "INDEX", "CONCURRENTLY"]),
      new("DROP INDEX CONCURRENTLY", ["DROP", "INDEX", "CONCURRENTLY"]),
      new("REINDEX CONCURRENTLY", ["REINDEX"], thenAnywhereInOrder: ["CONCURRENTLY"]),
      new("DETACH PARTITION CONCURRENTLY", ["ALTER", "TABLE"], thenAnywhereInOrder: ["DETACH", "PARTITION", "CONCURRENTLY"]),
      new("VACUUM", ["VACUUM"]),
      new("CREATE DATABASE", ["CREATE", "DATABASE"]),
      new("DROP DATABASE", ["DROP", "DATABASE"]),
      new("CREATE TABLESPACE", ["CREATE", "TABLESPACE"]),
      new("DROP TABLESPACE", ["DROP", "TABLESPACE"]),
      new("ALTER SYSTEM", ["ALTER", "SYSTEM"])
   ];

   public static string? FindFirst(string sql)
   {
      foreach (var statement in _sqlComments.Replace(sql, " ").Split(';'))
      {
         var words = _wordSeparators.Split(statement).Where(word => word.Length > 0).ToArray();
         var match = _patterns.FirstOrDefault(pattern => pattern.Matches(words));

         if (match is not null)
            return match.Name;
      }

      return null;
   }

   private sealed class StatementPattern(string name, string[] startsWith, string[]? thenAnywhereInOrder = null)
   {
      private readonly string[] _thenAnywhereInOrder = thenAnywhereInOrder ?? [];

      public string Name { get; } = name;

      public bool Matches(string[] words)
      {
         if (words.Length < startsWith.Length)
            return false;

         for (var i = 0; i < startsWith.Length; i++)
         {
            if (!IsWord(words[i], startsWith[i]))
               return false;
         }

         var next = startsWith.Length;

         foreach (var word in _thenAnywhereInOrder)
         {
            while (next < words.Length && !IsWord(words[next], word))
               next++;

            if (next == words.Length)
               return false;

            next++;
         }

         return true;
      }

      private static bool IsWord(string actual, string expected)
      {
         return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
      }
   }
}
