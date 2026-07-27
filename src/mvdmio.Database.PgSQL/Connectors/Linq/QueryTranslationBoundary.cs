namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Wraps every path on which the query surface can fail, so a provider exception is translated into this
///    library's contract no matter how the query was executed — including a framework enumerating it directly.
/// </summary>
internal static class QueryTranslationBoundary
{
   public static T Execute<T>(Func<T> action, LinqQuerySource source)
   {
      var sqlBeforeExecuting = source.GetLastSql();

      try
      {
         return action.Invoke();
      }
      catch (Exception exception) when (QueryExceptionTranslator.ShouldTranslate(exception))
      {
         throw QueryExceptionTranslator.Translate(exception, () => SqlThisExecutionSent(source, sqlBeforeExecuting));
      }
   }

   public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, LinqQuerySource source)
   {
      var sqlBeforeExecuting = source.GetLastSql();

      try
      {
         return await action.Invoke().ConfigureAwait(false);
      }
      catch (Exception exception) when (QueryExceptionTranslator.ShouldTranslate(exception))
      {
         throw QueryExceptionTranslator.Translate(exception, () => SqlThisExecutionSent(source, sqlBeforeExecuting));
      }
   }

   public static IEnumerable<T> Guard<T>(Func<IEnumerator<T>> enumeratorFactory, LinqQuerySource source)
   {
      var enumerator = Execute(enumeratorFactory, source);

      try
      {
         while (Execute(enumerator.MoveNext, source))
         {
            yield return enumerator.Current;
         }
      }
      finally
      {
         enumerator.Dispose();
      }
   }

   public static async IAsyncEnumerable<T> GuardAsync<T>(Func<IAsyncEnumerator<T>> enumeratorFactory, LinqQuerySource source)
   {
      var enumerator = Execute(enumeratorFactory, source);

      try
      {
         while (await ExecuteAsync(() => enumerator.MoveNextAsync().AsTask(), source).ConfigureAwait(false))
         {
            yield return enumerator.Current;
         }
      }
      finally
      {
         await enumerator.DisposeAsync().ConfigureAwait(false);
      }
   }

   /// <summary>
   ///    The SQL the failed execution itself sent, or null when it never got as far as sending any. Without the
   ///    comparison a failure that happened before the command was built would report the previous query's SQL.
   /// </summary>
   private static string? SqlThisExecutionSent(LinqQuerySource source, string? sqlBeforeExecuting)
   {
      var sql = source.GetLastSql();

      return string.Equals(sql, sqlBeforeExecuting, StringComparison.Ordinal) ? null : sql;
   }
}
