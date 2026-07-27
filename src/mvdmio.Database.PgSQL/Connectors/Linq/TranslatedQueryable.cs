using LinqToDB;
using LinqToDB.Internal.Async;
using System.Collections;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    The queryable and query provider handed to consumers by the query surface. It keeps the composed expression
///    unbound until execution — so the query runs against the connection and transaction current at that moment —
///    and it is the single place where a provider failure becomes one of this library's exceptions.
/// </summary>
/// <typeparam name="TElement">The element type of this stage of the composition.</typeparam>
/// <remarks>
///    <see cref="IQueryProviderAsync" /> lives in one of the provider's <c>Internal</c> namespaces, but implementing
///    it is not optional: the provider's asynchronous operators dispatch on it, and a provider that lacks it silently
///    degrades every awaited query to synchronous enumeration. The unit tests hold that behaviour in place.
/// </remarks>
internal class TranslatedQueryable<TElement> : IOrderedQueryable<TElement>, IAsyncEnumerable<TElement>, IQueryProviderAsync, ITranslatedQueryable, ISqlDiagnostics
{
   private readonly LinqQuerySource _source;
   private readonly Expression _expression;

   /// <summary>
   ///    Creates the root of a composition: the table itself, standing in for its own expression.
   /// </summary>
   public TranslatedQueryable(LinqQuerySource source)
   {
      ArgumentNullException.ThrowIfNull(source);

      _source = source;
      _expression = Expression.Constant(this);
      IsRoot = true;
   }

   /// <summary>
   ///    Creates a composed stage over the same source.
   /// </summary>
   public TranslatedQueryable(LinqQuerySource source, Expression expression)
   {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(expression);

      _source = source;
      _expression = expression;
      IsRoot = false;
   }

   public bool IsRoot { get; }

   public Type ElementType => typeof(TElement);
   public Expression Expression => _expression;
   public IQueryProvider Provider => this;
   public LinqQuerySource Source => _source;

   public IEnumerator<TElement> GetEnumerator()
   {
      return QueryTranslationBoundary
         .Guard(() => Materialize().GetEnumerator(), _source)
         .GetEnumerator();
   }

   IEnumerator IEnumerable.GetEnumerator()
   {
      return GetEnumerator();
   }

   public IAsyncEnumerator<TElement> GetAsyncEnumerator(CancellationToken cancellationToken = default)
   {
      return QueryTranslationBoundary
         .GuardAsync(() => MaterializeAsync().GetAsyncEnumerator(cancellationToken), _source)
         .GetAsyncEnumerator(cancellationToken);
   }

   /// <remarks>
   ///    A projection changes the element type, and only the expression knows the new one, so this overload has to
   ///    construct the closed generic type reflectively. Every path this library's own API uses — the LINQ operators
   ///    and the materialization extensions — goes through the generic overload below instead; only a caller that
   ///    composes through the untyped <see cref="IQueryable" /> reaches this one.
   /// </remarks>
   public IQueryable CreateQuery(Expression expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      var elementType = GetSequenceElementType(expression.Type);

      if (elementType == typeof(TElement))
         return CreateQuery<TElement>(expression);

      var queryableType = typeof(TranslatedQueryable<>).MakeGenericType(elementType);

      return (IQueryable)Activator.CreateInstance(
         queryableType,
         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
         binder: null,
         args: [_source, expression],
         culture: null
      )!;
   }

   public virtual IQueryable<TOther> CreateQuery<TOther>(Expression expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      return new TranslatedQueryable<TOther>(_source, expression);
   }

   /// <summary>
   ///    Records a materialization request on this composition, without binding anything to a connection yet.
   /// </summary>
   /// <typeparam name="TProperty">The type of the relation property being materialized.</typeparam>
   /// <param name="step">The provider call that materializes the relation.</param>
   /// <returns>The composition, remembering the relation so a further level can be chained onto it.</returns>
   public IIncludedQueryable<TElement, TProperty> Including<TProperty>(IncludeStep step)
   {
      ArgumentNullException.ThrowIfNull(step);

      return new IncludedQueryable<TElement, TProperty>(_source, IncludeRewriter.Record<TElement>(_expression, step));
   }

   public object? Execute(Expression expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      return QueryTranslationBoundary.Execute(
         () =>
         {
            var (provider, rewritten) = Resolve(expression);
            return provider.Execute(rewritten);
         },
         _source
      );
   }

   public TResult Execute<TResult>(Expression expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      return QueryTranslationBoundary.Execute(
         () =>
         {
            var (provider, rewritten) = Resolve(expression);
            return provider.Execute<TResult>(rewritten);
         },
         _source
      );
   }

   public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
   {
      ArgumentNullException.ThrowIfNull(expression);

      return QueryTranslationBoundary.ExecuteAsync(
         () =>
         {
            var (provider, rewritten) = Resolve(expression);
            return AsAsyncProvider(provider).ExecuteAsync<TResult>(rewritten, cancellationToken);
         },
         _source
      );
   }

   public Task<IAsyncEnumerable<TResult>> ExecuteAsyncEnumerable<TResult>(Expression expression, CancellationToken cancellationToken)
   {
      ArgumentNullException.ThrowIfNull(expression);

      return QueryTranslationBoundary.ExecuteAsync<IAsyncEnumerable<TResult>>(
         async () =>
         {
            var (provider, rewritten) = Resolve(expression);
            var sequence = await AsAsyncProvider(provider).ExecuteAsyncEnumerable<TResult>(rewritten, cancellationToken).ConfigureAwait(false);

            return QueryTranslationBoundary.GuardAsync(() => sequence.GetAsyncEnumerator(cancellationToken), _source);
         },
         _source
      );
   }

   /// <summary>
   ///    Renders the SQL this query translates to. Used to prove that runtime values become parameters.
   /// </summary>
   public string RenderSql()
   {
      return QueryTranslationBoundary.Execute(() => Materialize().ToSqlQuery().Sql, _source);
   }

   /// <summary>
   ///    The SQL most recently sent to the database. Used to prove that an aggregate was answered by the database
   ///    rather than by materializing rows.
   /// </summary>
   public string? LastSql()
   {
      return _source.GetLastSql();
   }

   private IQueryable<TElement> Materialize()
   {
      var (provider, rewritten) = Resolve(_expression);

      return provider.CreateQuery<TElement>(rewritten);
   }

   private IAsyncEnumerable<TElement> MaterializeAsync()
   {
      var materialized = Materialize();

      if (materialized is IAsyncEnumerable<TElement> asyncEnumerable)
         return asyncEnumerable;

      throw new NotSupportedException($"The query provider did not produce an asynchronously enumerable query for '{typeof(TElement)}'.");
   }

   /// <remarks>
   ///    Materialization is translated here rather than where it was composed, so that everything the decorator
   ///    guarantees survives a query that asked for it. See <see cref="IncludeRewriter" />.
   /// </remarks>
   private (IQueryProvider Provider, Expression Expression) Resolve(Expression expression)
   {
      var rewriter = new QueryRootRewriter();
      var root = rewriter.ResolveRoot(_source);
      var materialized = IncludeRewriter.Rewrite(expression, (innermost, steps) => ApplyIncludes(rewriter, root, innermost, steps));

      return (root.Provider, rewriter.Rewrite(materialized));
   }

   private static Expression ApplyIncludes(QueryRootRewriter rewriter, IQueryable root, Expression innermost, ImmutableArray<IncludeStep> steps)
   {
      var queryable = root.Provider.CreateQuery(rewriter.Rewrite(innermost));

      foreach (var step in steps)
      {
         queryable = step.Apply(queryable, rewriter);
      }

      return queryable.Expression;
   }

   private static IQueryProviderAsync AsAsyncProvider(IQueryProvider provider)
   {
      if (provider is IQueryProviderAsync asyncProvider)
         return asyncProvider;

      throw new NotSupportedException($"The query provider '{provider.GetType()}' does not support asynchronous execution.");
   }

   private static Type GetSequenceElementType(Type expressionType)
   {
      if (expressionType.IsGenericType && expressionType.GetGenericArguments().Length == 1)
         return expressionType.GetGenericArguments()[0];

      var enumerable = expressionType.GetInterfaces()
         .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>));

      if (enumerable is not null)
         return enumerable.GetGenericArguments()[0];

      throw new ArgumentException($"Cannot determine the element type of '{expressionType}'.", nameof(expressionType));
   }
}
