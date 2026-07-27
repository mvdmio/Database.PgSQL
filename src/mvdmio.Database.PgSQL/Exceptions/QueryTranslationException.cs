using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Exceptions;

/// <summary>
///   Exception thrown when a query composed on the query surface cannot be translated to SQL.
///   No SQL exists yet at this point, which is what separates it from <see cref="QueryException" />.
/// </summary>
[PublicAPI]
public sealed class QueryTranslationException : DatabaseException
{
   /// <inheritdoc />
   public QueryTranslationException(Exception inner)
      : base($"The query could not be translated to SQL. {inner.Message}", inner)
   {
   }
}
