namespace mvdmio.Database.PgSQL.Analyzers;

internal static class TableRepositorySqlStatements
{
   public static string BuildCreateSql(TableDefinitionModel model)
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

   public static string BuildGetAllSql(TableDefinitionModel model)
   {
      var constraint = model.TenancyConstraint;
      if (constraint.IsEmpty)
         return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}";

      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(constraint, x => x.ParameterName)}";
   }

   public static string BuildGetBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(model.LookupConstraint(property), x => x.ParameterName)}";
   }

   public static string BuildGetByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(model.PrimaryKeyConstraint, x => x.ParameterName)}";
   }

   public static string BuildUpdateSql(TableDefinitionModel model)
   {
      var assignments = string.Join(", ", model.MutableUpdateProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{x.PropertyName}"));
      return $"UPDATE {FullyQualifiedTableName(model)}\nSET {assignments}\nWHERE {BuildPredicate(model.PrimaryKeyConstraint, x => x.PropertyName)}\nRETURNING {BuildReturningList(model)}";
   }

   public static string BuildDeleteBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(model.LookupConstraint(property), x => x.ParameterName)}";
   }

   public static string BuildDeleteByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(model.PrimaryKeyConstraint, x => x.ParameterName)}";
   }

   private static string BuildPredicate(ConstrainedColumns constraint, Func<PropertyDefinitionModel, string> bindingName)
   {
      return string.Join(" AND ", constraint.InStatementOrder.Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{bindingName(x)}"));
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
