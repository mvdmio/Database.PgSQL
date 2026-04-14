using System.Text;

namespace mvdmio.Database.PgSQL.Connectors.Schema;

internal static class SchemaScriptTableRenderer
{
   public static void AppendTables(StringBuilder sb, IReadOnlyList<Models.TableInfo> tables, IReadOnlyList<Models.ConstraintInfo> constraints)
   {
      if (tables.Count == 0)
         return;

      var inlineConstraintsByTable = constraints
         .Where(CanInlineConstraint)
         .GroupBy(c => GetTableKey(c.Schema, c.TableName))
         .ToDictionary(g => g.Key, g => g.OrderBy(c => GetConstraintOrder(c.ConstraintType)).ThenBy(c => c.ConstraintName).ToArray());

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Tables");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var table in tables)
      {
         var tableKey = GetTableKey(table.Schema, table.Name);
         var tableConstraints = inlineConstraintsByTable.GetValueOrDefault(tableKey) ?? [];

         sb.AppendLine($"CREATE TABLE IF NOT EXISTS \"{table.Schema}\".\"{table.Name}\" (");

         var lines = new List<string>(table.Columns.Count + tableConstraints.Length);

         for (var i = 0; i < table.Columns.Count; i++)
         {
            var col = table.Columns[i];
            var line = $"   \"{col.Name}\" {col.DataType}";

            if (col.IsIdentity)
               line += $" GENERATED {col.IdentityGeneration} AS IDENTITY";
            else if (col.IsGeneratedStored)
               line += $" GENERATED ALWAYS AS ({col.GeneratedExpression}) STORED";
            else if (col.DefaultValue is not null)
               line += $" DEFAULT {col.DefaultValue}";

            if (!col.IsNullable)
               line += " NOT NULL";

            lines.Add(line);
         }

         lines.AddRange(tableConstraints.Select(c => $"   CONSTRAINT \"{c.ConstraintName}\" {c.Definition}"));

         for (var i = 0; i < lines.Count; i++)
         {
            var line = i < lines.Count - 1 ? $"{lines[i]}," : lines[i];
            sb.AppendLine(line);
         }

         sb.AppendLine(");");
         sb.AppendLine();
      }
   }

   public static void AppendConstraints(StringBuilder sb, IReadOnlyList<Models.ConstraintInfo> constraints, Func<string?, string> escapeSqlString)
   {
      var remainingConstraints = constraints.Where(c => !CanInlineConstraint(c) && !ShouldSkipConstraint(c)).ToArray();

      if (remainingConstraints.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Constraints");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      var ordered = remainingConstraints.OrderBy(c => GetConstraintOrder(c.ConstraintType)).ThenBy(c => c.Schema).ThenBy(c => c.TableName).ThenBy(c => c.ConstraintName);

      foreach (var constraint in ordered)
      {
         if (constraint.ConstraintName is null || constraint.Schema is null || constraint.TableName is null || constraint.Definition is null)
            continue;

         var tableRegClass = $"\"{constraint.Schema}\".\"{constraint.TableName}\"";

         sb.AppendLine("DO $$ BEGIN");
         sb.AppendLine($"   IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = '{escapeSqlString(constraint.ConstraintName)}' AND conrelid = '{escapeSqlString(tableRegClass)}'::regclass) THEN");
         sb.AppendLine($"      ALTER TABLE \"{constraint.Schema}\".\"{constraint.TableName}\" ADD CONSTRAINT \"{constraint.ConstraintName}\" {constraint.Definition};");
         sb.AppendLine("   END IF;");
         sb.AppendLine("END $$;");
         sb.AppendLine();
      }
   }

   private static bool CanInlineConstraint(Models.ConstraintInfo constraint)
   {
      return constraint.ConstraintType is "p" or "u" or "c" or "x";
   }

   private static bool ShouldSkipConstraint(Models.ConstraintInfo constraint)
   {
      return constraint.Definition.StartsWith("NOT NULL ", StringComparison.OrdinalIgnoreCase);
   }

   private static int GetConstraintOrder(string? constraintType)
   {
      return constraintType switch
      {
         "p" => 0,
         "u" => 1,
         "c" => 2,
         "f" => 3,
         "x" => 4,
         _ => 99
      };
   }

   private static string GetTableKey(string schema, string tableName)
   {
      return $"{schema}.{tableName}";
   }
}
