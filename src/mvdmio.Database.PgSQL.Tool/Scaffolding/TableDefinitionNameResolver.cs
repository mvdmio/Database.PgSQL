using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using System.Text;

namespace mvdmio.Database.PgSQL.Tool.Scaffolding;

internal static class TableDefinitionNameResolver
{
   public static Dictionary<string, string> BuildClassNames(IReadOnlyList<TableInfo> tables)
   {
      var result = new Dictionary<string, string>(StringComparer.Ordinal);
      var baseNameGroups = tables.GroupBy(x => ToPascalIdentifier(x.Name) + "Table", StringComparer.Ordinal);

      foreach (var group in baseNameGroups)
      {
         if (group.Count() == 1)
         {
            var table = group.Single();
            result[GetTableKey(table.Schema, table.Name)] = group.Key;
            continue;
         }

         var usedNames = new HashSet<string>(StringComparer.Ordinal);
         foreach (var table in group)
         {
            var candidate = ToPascalIdentifier(table.Schema) + group.Key;
            var uniqueCandidate = candidate;
            var suffix = 2;

            while (!usedNames.Add(uniqueCandidate))
            {
               uniqueCandidate = candidate + suffix;
               suffix++;
            }

            result[GetTableKey(table.Schema, table.Name)] = uniqueCandidate;
         }
      }

      return result;
   }

   public static string GetTableKey(string schema, string tableName)
   {
      return schema + "." + tableName;
   }

   public static string ToPascalIdentifier(string value)
   {
      var builder = new StringBuilder(value.Length + 4);
      var capitalizeNext = true;

      foreach (var character in value)
      {
         if (!char.IsLetterOrDigit(character))
         {
            capitalizeNext = true;
            continue;
         }

         if (builder.Length == 0 && char.IsDigit(character))
            builder.Append('_');

         builder.Append(capitalizeNext ? char.ToUpperInvariant(character) : character);
         capitalizeNext = false;
      }

      return builder.Length == 0 ? "Table" : builder.ToString();
   }

   public static string ToSnakeCase(string value)
   {
      var builder = new StringBuilder(value.Length + 5);

      for (var i = 0; i < value.Length; i++)
      {
         var current = value[i];
         if (char.IsUpper(current))
         {
            if (i > 0)
               builder.Append('_');

            builder.Append(char.ToLowerInvariant(current));
            continue;
         }

         builder.Append(current);
      }

      return builder.ToString();
   }
}
