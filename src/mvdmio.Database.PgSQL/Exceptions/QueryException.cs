namespace mvdmio.Database.PgSQL.Exceptions;

/// <summary>
///   Exception thrown when a query fails.
/// </summary>
public class QueryException : DatabaseException
{
   /// <summary>
   ///   The SQL query that caused the exception.
   /// </summary>
   public string Sql { get; }

   /// <inheritdoc />
   public QueryException(string sql)
      : base("Error while executing SQL.")
   {
      Sql = sql;
   }

   /// <inheritdoc />
   public QueryException(string sql, Exception inner)
      : base("Error while executing SQL.", inner)
   {
      Sql = sql;
   }
}