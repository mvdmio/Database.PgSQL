using JetBrains.Annotations;
using LinqToDB;
using LinqToDB.Async;
using mvdmio.Database.PgSQL.Connectors.Linq;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    Awaitable materialization and relation materialization for the queryables returned by generated repositories, so
///    a query can be executed and can pull in related rows without blocking a request thread and without importing the
///    query provider's own namespace.
/// </summary>
[PublicAPI]
public static class QueryableExtensions
{
   /// <summary>
   ///    Materializes a relation along with the rows of the query. Without this the relation property stays null, or
   ///    empty for a collection: nothing is loaded behind your back.
   /// </summary>
   /// <typeparam name="TEntity">The element type of the query.</typeparam>
   /// <typeparam name="TProperty">The type of the relation property.</typeparam>
   /// <param name="source">The query to materialize the relation on.</param>
   /// <param name="property">An expression selecting the relation property to materialize.</param>
   /// <returns>The query, remembering the relation so a further level can be chained onto it.</returns>
   /// <remarks>
   ///    A relation to one row folds into the query as an outer join. A relation to many rows costs one extra
   ///    statement per level, each of which re-runs the query above it as a derived table.
   /// </remarks>
   public static IIncludedQueryable<TEntity, TProperty> Include<TEntity, TProperty>(this IQueryable<TEntity> source, Expression<Func<TEntity, TProperty?>> property)
      where TEntity : class
   {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(property);

      return Record<TEntity, TProperty>(source, new IncludeStep((queryable, rewriter) => ((IQueryable<TEntity>)queryable).LoadWith(rewriter.Rewrite(property))));
   }

   /// <summary>
   ///    Materializes a relation to many rows along with the rows of the query, loading only the related rows
   ///    <paramref name="filter" /> keeps.
   /// </summary>
   /// <typeparam name="TEntity">The element type of the query.</typeparam>
   /// <typeparam name="TProperty">The element type of the relation property.</typeparam>
   /// <param name="source">The query to materialize the relation on.</param>
   /// <param name="property">An expression selecting the relation property to materialize.</param>
   /// <param name="filter">Scopes the related rows, independently of the query above it.</param>
   /// <returns>The query, remembering the relation so a further level can be chained onto it.</returns>
   public static IIncludedQueryable<TEntity, TProperty> Include<TEntity, TProperty>(
      this IQueryable<TEntity> source,
      Expression<Func<TEntity, IEnumerable<TProperty>>> property,
      Expression<Func<IQueryable<TProperty>, IQueryable<TProperty>>> filter
   )
      where TEntity : class
   {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(property);
      ArgumentNullException.ThrowIfNull(filter);

      return Record<TEntity, TProperty>(source, new IncludeStep((queryable, rewriter) => ((IQueryable<TEntity>)queryable).LoadWith(rewriter.Rewrite(property)!, rewriter.Rewrite(filter))));
   }

   /// <summary>
   ///    Materializes a relation of the rows the previous materialization loaded.
   /// </summary>
   /// <typeparam name="TEntity">The element type of the query.</typeparam>
   /// <typeparam name="TPreviousProperty">The type of the relation property most recently included.</typeparam>
   /// <typeparam name="TProperty">The type of the relation property to materialize.</typeparam>
   /// <param name="source">The query whose most recent materialization this one hangs off.</param>
   /// <param name="property">An expression selecting the relation property to materialize.</param>
   /// <returns>The query, remembering the relation so a further level can be chained onto it.</returns>
   public static IIncludedQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
      this IIncludedQueryable<TEntity, TPreviousProperty> source,
      Expression<Func<TPreviousProperty, TProperty?>> property
   )
      where TEntity : class
   {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(property);

      return Record<TEntity, TProperty>(source, new IncludeStep((queryable, rewriter) => ((ILoadWithQueryable<TEntity, TPreviousProperty>)queryable).ThenLoad(rewriter.Rewrite(property))));
   }

   /// <summary>
   ///    Materializes a relation of each row the previous materialization loaded into a collection.
   /// </summary>
   /// <typeparam name="TEntity">The element type of the query.</typeparam>
   /// <typeparam name="TPreviousProperty">The element type of the relation property most recently included.</typeparam>
   /// <typeparam name="TProperty">The type of the relation property to materialize.</typeparam>
   /// <param name="source">The query whose most recent materialization this one hangs off.</param>
   /// <param name="property">An expression selecting the relation property to materialize.</param>
   /// <returns>The query, remembering the relation so a further level can be chained onto it.</returns>
   public static IIncludedQueryable<TEntity, TProperty> ThenInclude<TEntity, TPreviousProperty, TProperty>(
      this IIncludedQueryable<TEntity, IEnumerable<TPreviousProperty>> source,
      Expression<Func<TPreviousProperty, TProperty?>> property
   )
      where TEntity : class
   {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(property);

      return Record<TEntity, TProperty>(source, new IncludeStep((queryable, rewriter) => ((ILoadWithQueryable<TEntity, IEnumerable<TPreviousProperty>>)queryable).ThenLoad(rewriter.Rewrite(property))));
   }

   /// <remarks>
   ///    The step is recorded into the composed expression rather than handed to the provider now, so the query stays
   ///    unbound until it executes. <see cref="IncludeRewriter" /> explains why that is not a preference.
   /// </remarks>
   private static IIncludedQueryable<TEntity, TProperty> Record<TEntity, TProperty>(IQueryable<TEntity> source, IncludeStep step)
      where TEntity : class
   {
      if (source is not TranslatedQueryable<TEntity> translated)
         throw new NotSupportedException($"Relations can only be materialized on a query from a generated repository's Query(), but this query is a '{source.GetType()}'.");

      return translated.Including<TProperty>(step);
   }

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
