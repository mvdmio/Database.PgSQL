using System;
using System.Collections.Generic;
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
      if (model.TenancyColumns.IsEmpty)
         return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}";

      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildTenancyPredicate(model)}";
   }

   /// <summary>Every tenancy column constrained, bound by the parameter name the caller supplies it under.</summary>
   private static string BuildTenancyPredicate(TableDefinitionModel model)
   {
      return string.Join(" AND ", model.TenancyColumns.Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{x.ParameterName}"));
   }

   public static string BuildGetBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildLookupPredicate(model, property)}";
   }

   public static string BuildGetByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildKeyAndTenancyPredicate(model, x => x.ParameterName)}";
   }

   public static string BuildUpdateSql(TableDefinitionModel model)
   {
      var assignments = string.Join(", ", model.MutableUpdateProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{x.PropertyName}"));
      return $"UPDATE {FullyQualifiedTableName(model)}\nSET {assignments}\nWHERE {BuildKeyPredicate(model, x => x.PropertyName)}\nRETURNING {BuildReturningList(model)}";
   }

   public static string BuildDeleteBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {BuildLookupPredicate(model, property)}";
   }

   public static string BuildDeleteByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {BuildKeyAndTenancyPredicate(model, x => x.ParameterName)}";
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

   /// <summary>
   ///    Every key member constrained, plus every tenancy column not already a key member — used by the two members
   ///    that address a row by its primary key alone. Identical to <see cref="BuildKeyPredicate" /> where every tenancy
   ///    column is already a key member, which is why a table safe by construction gains no predicate here.
   /// </summary>
   public static string BuildKeyAndTenancyPredicate(TableDefinitionModel model, Func<PropertyDefinitionModel, string> bindingName)
   {
      var predicates = model.PrimaryKeys.Concat(TenancyColumnsOutsideKey(model)).Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{bindingName(x)}");
      return string.Join(" AND ", predicates);
   }

   /// <summary>
   ///    A <c>[Unique]</c> column constrained, plus every tenancy column other than the one being looked up — the
   ///    lookup and delete a <c>[Unique]</c> property gets. Where the property itself carries <c>Tenancy = true</c>,
   ///    it is excluded from the tenancy half so its value is constrained once rather than twice.
   /// </summary>
   private static string BuildLookupPredicate(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      var predicates = new[] { $"{QuoteIdentifier(property.ColumnName)} = :{property.ParameterName}" }
         .Concat(TenancyColumnsExcept(model, property).Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{x.ParameterName}"));

      return string.Join(" AND ", predicates);
   }

   /// <summary>The tenancy columns not already a primary-key member, in declaration order.</summary>
   private static IEnumerable<PropertyDefinitionModel> TenancyColumnsOutsideKey(TableDefinitionModel model)
   {
      return model.TenancyColumns.Where(x => !model.PrimaryKeys.Contains(x));
   }

   /// <summary>The tenancy columns other than <paramref name="property" />, in declaration order.</summary>
   private static IEnumerable<PropertyDefinitionModel> TenancyColumnsExcept(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return model.TenancyColumns.Where(x => !ReferenceEquals(x, property));
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
