using JetBrains.Annotations;
using LinqToDB.Async;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    Awaitable materialization for the queryables returned by generated repositories, so a query can be executed
///    without blocking a request thread and without importing the query provider's own namespace.
/// </summary>
[PublicAPI]
public static class QueryableExtensions
{
   /// <summary>
   ///    Asynchronously materializes the query into a list.
   /// </summary>
   /// <typeparam name="T">The element type of the query.</typeparam>
   /// <param name="source">The query to materialize.</param>
   /// <param name="ct">An optional token to cancel the asynchronous operation. The default value is None.</param>
   /// <returns>The rows the query returned.</returns>
   public static async Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(source);

      var result = new List<T>();

      await foreach (var item in source.AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
      {
         result.Add(item);
      }

      return result;
   }

   /// <summary>
   ///    Asynchronously returns the first row of the query.
   /// </summary>
   /// <typeparam name="T">The element type of the query.</typeparam>
   /// <param name="source">The query to execute.</param>
   /// <param name="ct">An optional token to cancel the asynchronous operation. The default value is None.</param>
   /// <returns>The first row.</returns>
   /// <exception cref="InvalidOperationException">Thrown when the query returned no rows.</exception>
   public static Task<T> FirstAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(source);

      return AsyncExtensions.FirstAsync(source, ct);
   }

   /// <summary>
   ///    Asynchronously returns the first row of the query, or the default value when it returned none.
   /// </summary>
   /// <typeparam name="T">The element type of the query.</typeparam>
   /// <param name="source">The query to execute.</param>
   /// <param name="ct">An optional token to cancel the asynchronous operation. The default value is None.</param>
   /// <returns>The first row, or the default value.</returns>
   public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(source);

      return AsyncExtensions.FirstOrDefaultAsync(source, ct);
   }

   /// <summary>
   ///    Asynchronously returns the only row of the query.
   /// </summary>
   /// <typeparam name="T">The element type of the query.</typeparam>
   /// <param name="source">The query to execute.</param>
   /// <param name="ct">An optional token to cancel the asynchronous operation. The default value is None.</param>
   /// <returns>The only row.</returns>
   /// <exception cref="InvalidOperationException">Thrown when the query did not return exactly one row.</exception>
   public static Task<T> SingleAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(source);

      return AsyncExtensions.SingleAsync(source, ct);
   }

   /// <summary>
   ///    Asynchronously returns the only row of the query, or the default value when it returned none.
   /// </summary>
   /// <typeparam name="T">The element type of the query.</typeparam>
   /// <param name="source">The query to execute.</param>
   /// <param name="ct">An optional token to cancel the asynchronous operation. The default value is None.</param>
   /// <returns>The only row, or the default value.</returns>
   /// <exception cref="InvalidOperationException">Thrown when the query returned more than one row.</exception>
   public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(source);

      return AsyncExtensions.SingleOrDefaultAsync(source, ct);
   }

   /// <summary>
   ///    Asynchronously counts the rows the query returns.
   /// </summary>
   /// <typeparam name="T">The element type of the query.</typeparam>
   /// <param name="source">The query to execute.</param>
   /// <param name="ct">An optional token to cancel the asynchronous operation. The default value is None.</param>
   /// <returns>The number of rows.</returns>
   public static Task<int> CountAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(source);

      return AsyncExtensions.CountAsync(source, ct);
   }

   /// <summary>
   ///    Asynchronously counts the rows the query returns, as a 64-bit value.
   /// </summary>
   /// <typeparam name="T">The element type of the query.</typeparam>
   /// <param name="source">The query to execute.</param>
   /// <param name="ct">An optional token to cancel the asynchronous operation. The default value is None.</param>
   /// <returns>The number of rows.</returns>
   public static Task<long> LongCountAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(source);

      return AsyncExtensions.LongCountAsync(source, ct);
   }

   /// <summary>
   ///    Asynchronously determines whether the query returns any rows.
   /// </summary>
   /// <typeparam name="T">The element type of the query.</typeparam>
   /// <param name="source">The query to execute.</param>
   /// <param name="ct">An optional token to cancel the asynchronous operation. The default value is None.</param>
   /// <returns>True when the query returned at least one row.</returns>
   public static Task<bool> AnyAsync<T>(this IQueryable<T> source, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(source);

      return AsyncExtensions.AnyAsync(source, ct);
   }
}
