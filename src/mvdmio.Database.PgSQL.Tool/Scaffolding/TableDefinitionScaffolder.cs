using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using System.Text;

namespace mvdmio.Database.PgSQL.Tool.Scaffolding;

/// <summary>
///    Generates table definition classes from database schema metadata.
/// </summary>
internal static class TableDefinitionScaffolder
{
   public static TableDefinitionScaffoldingResult Generate(
      string tableNamespace,
      IEnumerable<TableInfo> tables,
      IEnumerable<ConstraintInfo> constraints
   )
   {
      var tableArray = tables.OrderBy(x => x.Schema, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();
      var constraintLookup = BuildConstraintLookup(constraints);
      var classNames = BuildClassNames(tableArray);

      var files = new List<ScaffoldedTableFile>();
      var warnings = new List<string>();

      foreach (var table in tableArray)
      {
         var key = GetTableKey(table.Schema, table.Name);
         var tableConstraints = constraintLookup.GetValueOrDefault(key) ?? new TableConstraintMetadata();
         var supportsRepositoryGeneration = tableConstraints.PrimaryKeyColumns.Count == 1;

         if (!supportsRepositoryGeneration)
         {
            var reason = tableConstraints.PrimaryKeyColumns.Count == 0
               ? "no primary key was found"
               : "composite primary keys are not supported";

            warnings.Add($"Skipped repository-ready attributes for {table.Schema}.{table.Name}: {reason}.");
         }

         files.Add(new ScaffoldedTableFile(
            FileName: $"{classNames[key]}.cs",
            Content: BuildContent(tableNamespace, classNames[key], table, tableConstraints, supportsRepositoryGeneration)
         ));
      }

      return new TableDefinitionScaffoldingResult(files, warnings);
   }

   private static string BuildContent(
      string tableNamespace,
      string className,
      TableInfo table,
      TableConstraintMetadata constraints,
      bool supportsRepositoryGeneration
   )
   {
      var builder = new StringBuilder();
      builder.AppendLine("using System;");
      builder.AppendLine("using mvdmio.Database.PgSQL.Attributes;");
      builder.AppendLine();
      builder.AppendLine($"namespace {tableNamespace};");
      builder.AppendLine();

      if (!supportsRepositoryGeneration)
      {
         builder.AppendLine("// Repository generation is skipped for this table because it does not have a single-column primary key.");
      }
      else
      {
         builder.AppendLine($"[Table(\"{table.Schema}.{table.Name}\")]");
      }

      builder.AppendLine($"public partial class {className}");
      builder.AppendLine("{");

      foreach (var column in table.Columns)
      {
         var propertyName = ToPascalIdentifier(column.Name);
         var expectedColumnName = ToSnakeCase(propertyName);

         if (supportsRepositoryGeneration && constraints.PrimaryKeyColumns.Contains(column.Name))
            builder.AppendLine("   [PrimaryKey]");

         if (supportsRepositoryGeneration && constraints.UniqueColumns.Contains(column.Name))
            builder.AppendLine("   [Unique]");

         if (IsGenerated(column))
            builder.AppendLine("   [Generated]");

         if (!string.Equals(expectedColumnName, column.Name, StringComparison.Ordinal))
            builder.AppendLine($"   [Column(\"{column.Name}\")]" );

         var typeName = GetClrTypeName(column, out var isReferenceType);
         var propertyType = column.IsNullable && IsNullableEligible(typeName, isReferenceType)
            ? AppendNullable(typeName, isReferenceType)
            : typeName;

         builder.Append($"   public {propertyType} {propertyName} {{ get; set; }}");

         var initializer = GetInitializer(typeName, isReferenceType, column.IsNullable);
         if (initializer is not null)
            builder.Append($" = {initializer};");

         builder.AppendLine();
         builder.AppendLine();
      }

      builder.AppendLine("}");
      return builder.ToString();
   }

   private static Dictionary<string, string> BuildClassNames(IReadOnlyList<TableInfo> tables)
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

   private static Dictionary<string, TableConstraintMetadata> BuildConstraintLookup(IEnumerable<ConstraintInfo> constraints)
   {
      var lookup = new Dictionary<string, TableConstraintMetadata>(StringComparer.Ordinal);

      foreach (var constraint in constraints)
      {
         var key = GetTableKey(constraint.Schema, constraint.TableName);
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
         {
            metadata.UniqueColumns.Add(columns[0]);
         }
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

   private static string GetClrTypeName(ColumnInfo column, out bool isReferenceType)
   {
      var dataType = column.DataType.Trim();
      if (dataType.EndsWith("[]", StringComparison.Ordinal))
      {
         var elementType = GetClrTypeName(new ColumnInfo
         {
            Name = column.Name,
            DataType = dataType[..^2],
            IsNullable = false,
            IsIdentity = false
         }, out _);

         isReferenceType = true;
         return $"{elementType}[]";
      }

      var normalized = dataType.ToLowerInvariant();
      isReferenceType = false;

      if (normalized.StartsWith("smallint", StringComparison.Ordinal) || normalized == "smallserial") return "short";
      if (normalized.StartsWith("integer", StringComparison.Ordinal) || normalized == "int" || normalized == "serial") return "int";
      if (normalized.StartsWith("bigint", StringComparison.Ordinal) || normalized == "bigserial") return "long";
      if (normalized.StartsWith("real", StringComparison.Ordinal)) return "float";
      if (normalized.StartsWith("double precision", StringComparison.Ordinal)) return "double";
      if (normalized.StartsWith("numeric", StringComparison.Ordinal) || normalized.StartsWith("decimal", StringComparison.Ordinal) || normalized == "money") return "decimal";
      if (normalized == "boolean") return "bool";
      if (normalized == "uuid") return "Guid";
      if (normalized == "date") return "DateOnly";
      if (normalized.StartsWith("timestamp", StringComparison.Ordinal)) return "DateTime";
      if (normalized == "time without time zone" || normalized.StartsWith("time(", StringComparison.Ordinal)) return "TimeOnly";
      if (normalized == "time with time zone") return "DateTimeOffset";
      if (normalized == "interval") return "TimeSpan";
      if (normalized == "bytea")
      {
         isReferenceType = true;
         return "byte[]";
      }

      isReferenceType = true;
      return "string";
   }

   private static bool IsNullableEligible(string typeName, bool isReferenceType)
   {
      return isReferenceType || typeName is "short" or "int" or "long" or "float" or "double" or "decimal" or "bool" or "Guid" or "DateOnly" or "DateTime" or "TimeOnly" or "DateTimeOffset" or "TimeSpan";
   }

   private static string AppendNullable(string typeName, bool isReferenceType)
   {
      return isReferenceType ? typeName + "?" : typeName + "?";
   }

   private static string? GetInitializer(string typeName, bool isReferenceType, bool isNullable)
   {
      if (isNullable)
         return null;

      if (typeName == "string")
         return "string.Empty";

      if (typeName == "byte[]")
         return "Array.Empty<byte>()";

      return isReferenceType ? "default!" : null;
   }

   private static bool IsGenerated(ColumnInfo column)
   {
      return column.IsIdentity || (column.DefaultValue?.StartsWith("nextval(", StringComparison.OrdinalIgnoreCase) ?? false);
   }

   private static string GetTableKey(string schema, string tableName)
   {
      return schema + "." + tableName;
   }

   private static string ToPascalIdentifier(string value)
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

   private static string ToSnakeCase(string value)
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

   private static string UnquoteIdentifier(string value)
   {
      return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
         ? value[1..^1].Replace("\"\"", "\"")
         : value;
   }

   internal sealed record TableDefinitionScaffoldingResult(
      IReadOnlyList<ScaffoldedTableFile> Files,
      IReadOnlyList<string> Warnings
   );

   internal sealed record ScaffoldedTableFile(
      string FileName,
      string Content
   );

   private sealed class TableConstraintMetadata
   {
      public List<string> PrimaryKeyColumns { get; } = [];
      public HashSet<string> UniqueColumns { get; } = new(StringComparer.Ordinal);
   }
}
