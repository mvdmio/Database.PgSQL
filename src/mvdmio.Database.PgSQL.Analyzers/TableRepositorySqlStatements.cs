namespace mvdmio.Database.PgSQL.Analyzers;

/// <summary>
///    The SQL text a generated repository issues. Kept apart from <see cref="TableRepositorySourceBuilder" />, which
///    owns the C# that carries these statements to Dapper — this type is text about PostgreSQL and touches nothing
///    about C#.
/// </summary>
internal static class TableRepositorySqlStatements
{
   /// <summary>
   ///    The insert <c>CreateAsync</c> issues. A table whose every column is database-generated has nothing to supply,
   ///    so it inserts default values rather than an empty column list, which is not valid PostgreSQL.
   /// </summary>
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

   /// <summary>
   ///    The select <c>GetAllAsync</c> issues: every row, narrowed to the tenant where the table declares one. Without
   ///    a tenancy column it carries no <c>WHERE</c> clause at all, which is the one member that used to have no
   ///    predicate to forget.
   /// </summary>
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

   /// <summary>The select the lookup named after <paramref name="property" /> issues.</summary>
   public static string BuildGetBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildLookupPredicate(model, property)}";
   }

   /// <summary>The select <c>GetByPrimaryKeyAsync</c> issues.</summary>
   public static string BuildGetByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildKeyAndTenancyPredicate(model, x => x.ParameterName)}";
   }

   /// <summary>
   ///    The update <c>UpdateAsync</c> issues. A tenancy column is never assigned, so a row cannot change tenant here;
   ///    where it sits outside the key it joins the <c>WHERE</c> clause instead, and an update aimed at another
   ///    tenant's row matches nothing.
   /// </summary>
   public static string BuildUpdateSql(TableDefinitionModel model)
   {
      var assignments = string.Join(", ", model.MutableUpdateProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{x.PropertyName}"));
      return $"UPDATE {FullyQualifiedTableName(model)}\nSET {assignments}\nWHERE {BuildKeyAndTenancyPredicate(model, x => x.PropertyName)}\nRETURNING {BuildReturningList(model)}";
   }

   /// <summary>The delete named after <paramref name="property" />, which addresses its row the way that lookup does.</summary>
   public static string BuildDeleteBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {BuildLookupPredicate(model, property)}";
   }

   /// <summary>The delete <c>DeleteByPrimaryKeyAsync</c> issues.</summary>
   public static string BuildDeleteByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {BuildKeyAndTenancyPredicate(model, x => x.ParameterName)}";
   }

   /// <summary>
   ///    Every key member constrained, plus every tenancy column not already a key member — used by the two members
   ///    that address a row by its primary key alone, and by the update statement's <c>WHERE</c> clause. Where every
   ///    tenancy column is already a key member, this constrains exactly the key, so a table safe by construction gains
   ///    no predicate here.
   /// </summary>
   /// <remarks>
   ///    <paramref name="bindingName" /> is which name the statement binds each member by. A lookup and a delete take
   ///    their values as method parameters and so bind by <see cref="PropertyDefinitionModel.ParameterName" />; an update
   ///    takes them off a command object alongside its other columns and binds by
   ///    <see cref="PropertyDefinitionModel.PropertyName" /> like the rest of that statement.
   /// </remarks>
   private static string BuildKeyAndTenancyPredicate(TableDefinitionModel model, Func<PropertyDefinitionModel, string> bindingName)
   {
      var predicates = model.PrimaryKeys.Concat(model.TenancyColumnsOutsideKey).Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{bindingName(x)}");
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
         .Concat(model.TenancyColumnsExcept(property).Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{x.ParameterName}"));

      return string.Join(" AND ", predicates);
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
