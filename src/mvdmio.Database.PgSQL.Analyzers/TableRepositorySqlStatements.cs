using System;
using System.Linq;

namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    The SQL text a generated repository issues. Kept apart from <see cref="TableRepositorySourceBuilder" />, which
///    owns the C# that carries these statements to Dapper — this type is text about PostgreSQL and touches nothing
///    about C#.
/// </summary>
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
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}";
   }

   public static string BuildGetBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {QuoteIdentifier(property.ColumnName)} = :{property.ParameterName}";
   }

   public static string BuildGetByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildKeyPredicate(model, x => x.ParameterName)}";
   }

   public static string BuildUpdateSql(TableDefinitionModel model)
   {
      var assignments = string.Join(", ", model.MutableUpdateProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{x.PropertyName}"));
      return $"UPDATE {FullyQualifiedTableName(model)}\nSET {assignments}\nWHERE {BuildKeyPredicate(model, x => x.PropertyName)}\nRETURNING {BuildReturningList(model)}";
   }

   public static string BuildDeleteBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {QuoteIdentifier(property.ColumnName)} = :{property.ParameterName}";
   }

   public static string BuildDeleteByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {BuildKeyPredicate(model, x => x.ParameterName)}";
   }

   /// <summary>
   ///    Every key member constrained, so a statement addressing a row by its primary key affects exactly one row.
   /// </summary>
   /// <remarks>
   ///    <paramref name="bindingName" /> is which name the statement binds each key member by. A lookup and a delete take
   ///    their values as method parameters and so bind by <see cref="PropertyDefinitionModel.ParameterName" />; an update
   ///    takes them off a command object alongside its other columns and binds by
   ///    <see cref="PropertyDefinitionModel.PropertyName" /> like the rest of that statement.
   /// </remarks>
   public static string BuildKeyPredicate(TableDefinitionModel model, Func<PropertyDefinitionModel, string> bindingName)
   {
      return string.Join(" AND ", model.PrimaryKeys.Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{bindingName(x)}"));
   }

   public static string BuildSelectList(TableDefinitionModel model)
   {
      return string.Join(", ", model.DataProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} AS {QuoteIdentifier(x.PropertyName)}"));
   }

   public static string BuildReturningList(TableDefinitionModel model)
   {
      return string.Join(", ", model.DataProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} AS {QuoteIdentifier(x.PropertyName)}"));
   }

   public static string FullyQualifiedTableName(TableDefinitionModel model)
   {
      return $"{QuoteIdentifier(model.SchemaName)}.{QuoteIdentifier(model.TableName)}";
   }

   public static string QuoteIdentifier(string value)
   {
      return $"\"{value.Replace("\"", "\"\"")}\"";
   }
}
