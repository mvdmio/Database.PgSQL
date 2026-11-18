namespace mvdmio.Database.PgSQL.Connectors.Linq;

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

   private static string? SqlThisExecutionSent(LinqQuerySource source, string? sqlBeforeExecuting)
   {
      var sql = source.GetLastSql();

      return string.Equals(sql, sqlBeforeExecuting, StringComparison.Ordinal) ? null : sql;
   }
}
