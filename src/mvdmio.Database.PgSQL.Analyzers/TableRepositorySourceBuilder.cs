using System.Collections.Immutable;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class TableRepositorySourceBuilder
{
   public static string Build(TableDefinitionModel model)
   {
      var builder = new StringBuilder();
      builder.AppendLine("#nullable enable");
      builder.AppendLine("using System;");
      builder.AppendLine("using System.Collections.Generic;");
      builder.AppendLine("using System.Threading;");
      builder.AppendLine("using System.Threading.Tasks;");

      if (!string.IsNullOrWhiteSpace(model.NamespaceName))
      {
         builder.AppendLine();
         builder.AppendLine($"namespace {model.NamespaceName};");
      }

      builder.AppendLine();
      AppendDto(builder, model.Accessibility, model.DataTypeName, model.DataProperties);
      builder.AppendLine();
      AppendDto(builder, model.Accessibility, model.CreateCommandTypeName, model.CreateProperties);
      builder.AppendLine();
      AppendDto(builder, model.Accessibility, model.UpdateCommandTypeName, model.UpdateProperties);
      builder.AppendLine();
      AppendRepositoryInterface(builder, model);
      builder.AppendLine();
      AppendRepository(builder, model);

      return builder.ToString();
   }

   private static void AppendDto(StringBuilder builder, string accessibility, string typeName, ImmutableArray<PropertyDefinitionModel> properties)
   {
      builder.AppendLine($"{accessibility} partial class {typeName}");
      builder.AppendLine("{");

      foreach (var property in properties)
      {
         builder.Append("   public ")
            .Append(property.TypeName)
            .Append(' ')
            .Append(property.PropertyName)
            .Append(" { get; set; }");

         if (property.RequiresNullForgivingInitializer)
            builder.Append(" = default!;");

         builder.AppendLine();
      }

      builder.AppendLine("}");
   }

   private static void AppendRepositoryInterface(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"{model.Accessibility} partial interface {model.RepositoryInterfaceTypeName}");
      builder.AppendLine("{");
      builder.AppendLine($"   Task<{model.DataTypeName}> CreateAsync({model.CreateCommandTypeName} data, CancellationToken ct = default);");
      builder.AppendLine($"   Task<IEnumerable<{model.DataTypeName}>> GetAllAsync(CancellationToken ct = default);");

      foreach (var property in model.LookupProperties)
      {
         builder.AppendLine($"   Task<{model.DataTypeName}?> GetBy{property.PropertyName}Async({property.TypeName} {property.ParameterName}, CancellationToken ct = default);");
      }

      builder.AppendLine($"   Task<{model.DataTypeName}> UpdateAsync({model.UpdateCommandTypeName} data, CancellationToken ct = default);");

      foreach (var property in model.LookupProperties)
      {
         builder.AppendLine($"   Task<bool> DeleteBy{property.PropertyName}Async({property.TypeName} {property.ParameterName}, CancellationToken ct = default);");
      }

      builder.AppendLine("}");
   }

   private static void AppendRepository(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"{model.Accessibility} partial class {model.RepositoryTypeName} : {model.RepositoryInterfaceTypeName}");
      builder.AppendLine("{");
      builder.AppendLine("   private readonly global::mvdmio.Database.PgSQL.DatabaseConnection _db;");
      builder.AppendLine();
      builder.AppendLine($"   public {model.RepositoryTypeName}(global::mvdmio.Database.PgSQL.DatabaseConnection db)");
      builder.AppendLine("   {");
      builder.AppendLine("      ArgumentNullException.ThrowIfNull(db);");
      builder.AppendLine("      _db = db;");
      builder.AppendLine("   }");
      builder.AppendLine();

      AppendCreateMethod(builder, model);
      builder.AppendLine();
      AppendGetAllMethod(builder, model);

      foreach (var property in model.LookupProperties)
      {
         builder.AppendLine();
         AppendGetByMethod(builder, model, property);
      }

      builder.AppendLine();
      AppendUpdateMethod(builder, model);

      foreach (var property in model.LookupProperties)
      {
         builder.AppendLine();
         AppendDeleteByMethod(builder, model, property);
      }

      builder.AppendLine("}");
   }

   private static void AppendCreateMethod(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"   public async Task<{model.DataTypeName}> CreateAsync({model.CreateCommandTypeName} data, CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine("      ArgumentNullException.ThrowIfNull(data);");
      builder.AppendLine();
      builder.AppendLine($"      return await _db.Dapper.QuerySingleAsync<{model.DataTypeName}>(");
      AppendSqlLiteral(builder, 9, BuildCreateSql(model));

      if (model.CreateProperties.Length == 0)
      {
         builder.AppendLine(",");
         builder.AppendLine("         ct: ct");
      }
      else
      {
         builder.AppendLine(",");
         AppendDictionary(builder, model.CreateProperties, "data", 9);
         builder.AppendLine(",");
         builder.AppendLine("         ct: ct");
      }

      builder.AppendLine("      );");
      builder.AppendLine("   }");
   }

   private static void AppendGetAllMethod(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"   public async Task<IEnumerable<{model.DataTypeName}>> GetAllAsync(CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine($"      return await _db.Dapper.QueryAsync<{model.DataTypeName}>(");
      AppendSqlLiteral(builder, 9, BuildGetAllSql(model));
      builder.AppendLine(",");
      builder.AppendLine("         ct: ct");
      builder.AppendLine("      );");
      builder.AppendLine("   }");
   }

   private static void AppendGetByMethod(StringBuilder builder, TableDefinitionModel model, PropertyDefinitionModel property)
   {
      builder.AppendLine($"   public async Task<{model.DataTypeName}?> GetBy{property.PropertyName}Async({property.TypeName} {property.ParameterName}, CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine($"      return await _db.Dapper.QuerySingleOrDefaultAsync<{model.DataTypeName}>(");
      AppendSqlLiteral(builder, 9, BuildGetBySql(model, property));
      builder.AppendLine(",");
      AppendSingleValueDictionary(builder, property.ParameterName, property.ParameterName, 9);
      builder.AppendLine(",");
      builder.AppendLine("         ct: ct");
      builder.AppendLine("      );");
      builder.AppendLine("   }");
   }

   private static void AppendUpdateMethod(StringBuilder builder, TableDefinitionModel model)
   {
      builder.AppendLine($"   public async Task<{model.DataTypeName}> UpdateAsync({model.UpdateCommandTypeName} data, CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine("      ArgumentNullException.ThrowIfNull(data);");
      builder.AppendLine();
      builder.AppendLine($"      return await _db.Dapper.QuerySingleAsync<{model.DataTypeName}>(");
      AppendSqlLiteral(builder, 9, BuildUpdateSql(model));
      builder.AppendLine(",");
      AppendDictionary(builder, model.UpdateProperties, "data", 9);
      builder.AppendLine(",");
      builder.AppendLine("         ct: ct");
      builder.AppendLine("      );");
      builder.AppendLine("   }");
   }

   private static void AppendDeleteByMethod(StringBuilder builder, TableDefinitionModel model, PropertyDefinitionModel property)
   {
      builder.AppendLine($"   public async Task<bool> DeleteBy{property.PropertyName}Async({property.TypeName} {property.ParameterName}, CancellationToken ct = default)");
      builder.AppendLine("   {");
      builder.AppendLine("      var affectedRows = await _db.Dapper.ExecuteAsync(");
      AppendSqlLiteral(builder, 9, BuildDeleteBySql(model, property));
      builder.AppendLine(",");
      AppendSingleValueDictionary(builder, property.ParameterName, property.ParameterName, 9);
      builder.AppendLine(",");
      builder.AppendLine("         ct: ct");
      builder.AppendLine("      );");
      builder.AppendLine();
      builder.AppendLine("      return affectedRows > 0;");
      builder.AppendLine("   }");
   }

   private static void AppendDictionary(StringBuilder builder, ImmutableArray<PropertyDefinitionModel> properties, string valueSource, int indentation)
   {
      builder.Append(' ', indentation).AppendLine("parameters: new Dictionary<string, object?>");
      builder.Append(' ', indentation).AppendLine("{");

      foreach (var property in properties)
      {
         builder.Append(' ', indentation + 3)
            .Append("[")
            .Append('"')
            .Append(property.PropertyName)
            .Append('"')
            .Append("] = ")
            .Append(valueSource)
            .Append('.')
            .Append(property.PropertyName)
            .AppendLine(",");
      }

      builder.Append(' ', indentation).Append('}');
   }

   private static void AppendSingleValueDictionary(StringBuilder builder, string key, string valueName, int indentation)
   {
      builder.Append(' ', indentation).AppendLine("parameters: new Dictionary<string, object?>");
      builder.Append(' ', indentation).AppendLine("{");
      builder.Append(' ', indentation + 3).Append("[").Append('"').Append(key).Append('"').Append("] = ").Append(valueName).AppendLine(",");
      builder.Append(' ', indentation).Append('}');
   }

   private static void AppendSqlLiteral(StringBuilder builder, int indentation, string sql)
   {
      builder.Append(' ', indentation).AppendLine("sql: \"\"\"");
      foreach (var line in sql.Split('\n'))
      {
         builder.Append(' ', indentation).AppendLine(line.TrimEnd('\r'));
      }

      builder.Append(' ', indentation).Append("\"\"\"");
   }

   private static string BuildCreateSql(TableDefinitionModel model)
   {
      var tableName = FullyQualifiedTableName(model);
      if (model.CreateProperties.Length == 0)
      {
         return $"INSERT INTO {tableName}\nDEFAULT VALUES\nRETURNING {BuildReturningList(model)}";
      }

      var columns = string.Join(", ", model.CreateProperties.Select(x => QuoteIdentifier(x.ColumnName)));
      var values = string.Join(", ", model.CreateProperties.Select(x => $":{x.PropertyName}"));
      return $"INSERT INTO {tableName} ({columns})\nVALUES ({values})\nRETURNING {BuildReturningList(model)}";
   }

   private static string BuildGetAllSql(TableDefinitionModel model)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}";
   }

   private static string BuildGetBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {QuoteIdentifier(property.ColumnName)} = :{property.ParameterName}";
   }

   private static string BuildUpdateSql(TableDefinitionModel model)
   {
      var assignments = string.Join(", ", model.MutableUpdateProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{x.PropertyName}"));
      return $"UPDATE {FullyQualifiedTableName(model)}\nSET {assignments}\nWHERE {QuoteIdentifier(model.PrimaryKey.ColumnName)} = :{model.PrimaryKey.PropertyName}\nRETURNING {BuildReturningList(model)}";
   }

   private static string BuildDeleteBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {QuoteIdentifier(property.ColumnName)} = :{property.ParameterName}";
   }

   private static string BuildSelectList(TableDefinitionModel model)
   {
      return string.Join(", ", model.DataProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} AS {QuoteIdentifier(x.PropertyName)}"));
   }

   private static string BuildReturningList(TableDefinitionModel model)
   {
      return string.Join(", ", model.DataProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} AS {QuoteIdentifier(x.PropertyName)}"));
   }

   private static string FullyQualifiedTableName(TableDefinitionModel model)
   {
      return $"{QuoteIdentifier(model.SchemaName)}.{QuoteIdentifier(model.TableName)}";
   }

   private static string QuoteIdentifier(string value)
   {
      return $"\"{value.Replace("\"", "\"\"")}\"";
   }
}
