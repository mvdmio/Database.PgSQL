using mvdmio.Database.PgSQL.Connectors.Schema.Models;

namespace mvdmio.Database.PgSQL.Tool.Scaffolding;

internal static class TableDefinitionConstraintAnalyzer
{
   public static Dictionary<string, TableConstraintMetadata> BuildLookup(IEnumerable<ConstraintInfo> constraints)
   {
      var lookup = new Dictionary<string, TableConstraintMetadata>(StringComparer.Ordinal);

      foreach (var constraint in constraints)
      {
         var key = TableDefinitionNameResolver.GetTableKey(constraint.Schema, constraint.TableName);
         if (!lookup.TryGetValue(key, out var metadata))
         {
            metadata = new TableConstraintMetadata();
            lookup[key] = metadata;
         }

         var columns = ExtractSimpleColumnList(constraint.Definition);
         if (columns.Count == 0)
            continue;

         if (string.Equals(constraint.ConstraintType, "p", StringComparison.Ordinal))
         {
            metadata.PrimaryKeyColumns.AddRange(columns);
            continue;
         }

         if (string.Equals(constraint.ConstraintType, "u", StringComparison.Ordinal) && columns.Count == 1)
            metadata.UniqueColumns.Add(columns[0]);
      }

      return lookup;
   }

   private static List<string> ExtractSimpleColumnList(string definition)
   {
      var openParen = definition.IndexOf('(');
      var closeParen = definition.LastIndexOf(')');
      if (openParen < 0 || closeParen <= openParen)
         return [];

      var contents = definition.Substring(openParen + 1, closeParen - openParen - 1);
      var parts = contents.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      var columns = new List<string>();

      foreach (var part in parts)
      {
         var candidate = part.Trim();
         if (candidate.Contains(' ') || candidate.Contains('('))
            return [];

         columns.Add(UnquoteIdentifier(candidate));
      }

      return columns;
   }

   private static string UnquoteIdentifier(string value)
   {
      return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
         ? value[1..^1].Replace("\"\"", "\"")
         : value;
   }
}

internal sealed class TableConstraintMetadata
{
   public List<string> PrimaryKeyColumns { get; } = [];
   public HashSet<string> UniqueColumns { get; } = new(StringComparer.Ordinal);
}
