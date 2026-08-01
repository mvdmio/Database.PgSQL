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
      var constraint = model.TenancyConstraint;
      if (constraint.IsEmpty)
         return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}";

      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(constraint, x => x.ParameterName)}";
   }

   /// <summary>The select the lookup named after <paramref name="property" /> issues.</summary>
   public static string BuildGetBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(model.LookupConstraint(property), x => x.ParameterName)}";
   }

   /// <summary>The select <c>GetByPrimaryKeyAsync</c> issues.</summary>
   public static string BuildGetByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"SELECT {BuildSelectList(model)}\nFROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(model.PrimaryKeyConstraint, x => x.ParameterName)}";
   }

   /// <summary>
   ///    The update <c>UpdateAsync</c> issues. A tenancy column is never assigned, so a row cannot change tenant here;
   ///    where it sits outside the key it joins the <c>WHERE</c> clause instead, and an update aimed at another
   ///    tenant's row matches nothing.
   /// </summary>
   public static string BuildUpdateSql(TableDefinitionModel model)
   {
      var assignments = string.Join(", ", model.MutableUpdateProperties.Select(x => $"{QuoteIdentifier(x.ColumnName)} = :{x.PropertyName}"));
      return $"UPDATE {FullyQualifiedTableName(model)}\nSET {assignments}\nWHERE {BuildPredicate(model.PrimaryKeyConstraint, x => x.PropertyName)}\nRETURNING {BuildReturningList(model)}";
   }

   /// <summary>The delete named after <paramref name="property" />, which addresses its row the way that lookup does.</summary>
   public static string BuildDeleteBySql(TableDefinitionModel model, PropertyDefinitionModel property)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(model.LookupConstraint(property), x => x.ParameterName)}";
   }

   /// <summary>The delete <c>DeleteByPrimaryKeyAsync</c> issues.</summary>
   public static string BuildDeleteByPrimaryKeySql(TableDefinitionModel model)
   {
      return $"DELETE FROM {FullyQualifiedTableName(model)}\nWHERE {BuildPredicate(model.PrimaryKeyConstraint, x => x.ParameterName)}";
   }

   /// <summary>
   ///    Every column <paramref name="constraint" /> names, constrained and joined with <c>AND</c>, in the order the
   ///    statement names them: what the member addresses its row by first, then the tenancy columns it adds. Which
   ///    columns those are is the model's question, so no statement here restates it.
   /// </summary>
   /// <remarks>
   ///    <paramref name="bindingName" /> is which name the statement binds each column by. A lookup and a delete take
   ///    their values as method parameters and so bind by <see cref="PropertyDefinitionModel.ParameterName" />; an update
   ///    takes them off a command object alongside its other columns and binds by
   ///    <see cref="PropertyDefinitionModel.PropertyName" /> like the rest of that statement.
   /// </remarks>
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
