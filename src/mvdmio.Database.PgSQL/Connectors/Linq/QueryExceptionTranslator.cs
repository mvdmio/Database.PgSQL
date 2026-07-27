using LinqToDB;
using mvdmio.Database.PgSQL.Exceptions;
using System.Data.Common;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Maps failures raised beneath the query surface onto this library's exception contract, so that no provider
///    exception type reaches a consumer's catch block.
/// </summary>
internal static class QueryExceptionTranslator
{
   private static readonly Assembly _providerAssembly = typeof(LinqToDBException).Assembly;

   /// <summary>
   ///    Whether <paramref name="exception" /> is one this library owns the contract for. Cancellation and plain
   ///    framework failures — an empty <c>First()</c>, a disposed connection — are left alone.
   /// </summary>
   public static bool ShouldTranslate(Exception exception)
   {
      if (exception is OperationCanceledException)
         return false;

      return IsProviderException(exception) || FindDatabaseException(exception) is not null;
   }

   /// <summary>
   ///    Translates a failure into either a <see cref="QueryException" /> carrying the SQL that reached the database,
   ///    or a <see cref="QueryTranslationException" /> for an expression that never got that far.
   /// </summary>
   public static DatabaseException Translate(Exception exception, Func<string?> lastSqlAccessor)
   {
      var databaseException = FindDatabaseException(exception);

      if (databaseException is null)
         return new QueryTranslationException(exception);

      var sql = lastSqlAccessor.Invoke();

      return new QueryException(string.IsNullOrWhiteSpace(sql) ? "<SQL was not captured>" : sql, exception);
   }

   private static bool IsProviderException(Exception exception)
   {
      for (var current = exception; current is not null; current = current.InnerException)
      {
         if (current.GetType().Assembly == _providerAssembly)
            return true;
      }

      return false;
   }

   private static DbException? FindDatabaseException(Exception exception)
   {
      for (var current = exception; current is not null; current = current.InnerException)
      {
         if (current is DbException databaseException)
            return databaseException;
      }

      return null;
   }
}
