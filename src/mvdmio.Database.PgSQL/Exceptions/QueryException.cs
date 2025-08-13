﻿using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Extensions;

namespace mvdmio.Database.PgSQL.Exceptions;

/// <summary>
///   Exception thrown when a query fails.
/// </summary>
[PublicAPI]
public class QueryException : DatabaseException
{
   /// <summary>
   ///   The SQL query that caused the exception.
   /// </summary>
   public string Sql { get; }

   /// <inheritdoc />
   public QueryException(string sql, Exception inner)
      : base("Error while executing SQL.", inner)
   {
      Sql = sql;
   }

   public override string ToString()
   {
      return $"""
         {Message}
         SQL:
            {Sql.Indented().TrimIncludingNewLines()}
         Exception:
            {InnerException!.ToString().Indented().TrimIncludingNewLines()}
         """;
   }
}
