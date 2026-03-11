using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using System.Text;

namespace mvdmio.Database.PgSQL.Tool.Scaffolding;

internal static class TableDefinitionContentBuilder
{
   public static string Build(
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
         AppendProperty(builder, column, constraints, supportsRepositoryGeneration);
      }

      builder.AppendLine("}");
      return builder.ToString();
   }

   private static void AppendProperty(
      StringBuilder builder,
      ColumnInfo column,
      TableConstraintMetadata constraints,
      bool supportsRepositoryGeneration
   )
   {
      var propertyName = TableDefinitionNameResolver.ToPascalIdentifier(column.Name);
      var expectedColumnName = TableDefinitionNameResolver.ToSnakeCase(propertyName);

      if (supportsRepositoryGeneration && constraints.PrimaryKeyColumns.Contains(column.Name))
         builder.AppendLine("   [PrimaryKey]");

      if (supportsRepositoryGeneration && constraints.UniqueColumns.Contains(column.Name))
         builder.AppendLine("   [Unique]");

      if (IsGenerated(column))
         builder.AppendLine("   [Generated]");

      if (!string.Equals(expectedColumnName, column.Name, StringComparison.Ordinal))
         builder.AppendLine($"   [Column(\"{column.Name}\")]");

      var typeName = GetClrTypeName(column, out var isReferenceType);
      var propertyType = column.IsNullable && IsNullableEligible(typeName)
         ? AppendNullable(typeName)
         : typeName;

      builder.Append($"   public {propertyType} {propertyName} {{ get; set; }}");

      var initializer = GetInitializer(typeName, isReferenceType, column.IsNullable);
      if (initializer is not null)
         builder.Append($" = {initializer};");

      builder.AppendLine();
      builder.AppendLine();
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

      if (normalized.StartsWith("smallint", StringComparison.Ordinal) || normalized == "smallserial")
         return "short";
      if (normalized.StartsWith("integer", StringComparison.Ordinal) || normalized == "int" || normalized == "serial")
         return "int";
      if (normalized.StartsWith("bigint", StringComparison.Ordinal) || normalized == "bigserial")
         return "long";
      if (normalized.StartsWith("real", StringComparison.Ordinal))
         return "float";
      if (normalized.StartsWith("double precision", StringComparison.Ordinal))
         return "double";
      if (normalized.StartsWith("numeric", StringComparison.Ordinal) || normalized.StartsWith("decimal", StringComparison.Ordinal) || normalized == "money")
         return "decimal";
      if (normalized == "boolean")
         return "bool";
      if (normalized == "uuid")
         return "Guid";
      if (normalized == "date")
         return "DateOnly";
      if (normalized.StartsWith("timestamp", StringComparison.Ordinal))
         return "DateTime";
      if (normalized == "time without time zone" || normalized.StartsWith("time(", StringComparison.Ordinal))
         return "TimeOnly";
      if (normalized == "time with time zone")
         return "DateTimeOffset";
      if (normalized == "interval")
         return "TimeSpan";
      if (normalized == "bytea")
      {
         isReferenceType = true;
         return "byte[]";
      }

      isReferenceType = true;
      return "string";
   }

   private static bool IsNullableEligible(string typeName)
   {
      return typeName is "string" or "byte[]" or "short" or "int" or "long" or "float" or "double" or "decimal" or "bool" or "Guid" or "DateOnly" or "DateTime" or "TimeOnly" or "DateTimeOffset" or "TimeSpan"
         || typeName.EndsWith("[]", StringComparison.Ordinal);
   }

   private static string AppendNullable(string typeName)
   {
      return typeName + "?";
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
}
