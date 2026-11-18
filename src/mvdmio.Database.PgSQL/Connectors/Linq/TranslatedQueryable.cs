using LinqToDB;
using LinqToDB.Internal.Async;
using System.Collections;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal class TranslatedQueryable<TElement> : IOrderedQueryable<TElement>, IAsyncEnumerable<TElement>, IQueryProviderAsync, ITranslatedQueryable, ISqlDiagnostics
{
   private readonly LinqQuerySource _source;
   private readonly Expression _expression;

   public TranslatedQueryable(LinqQuerySource source)
   {
      ArgumentNullException.ThrowIfNull(source);

      _source = source;
      _expression = Expression.Constant(this);
      IsRoot = true;
   }

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

   public string RenderSql()
   {
      return QueryTranslationBoundary.Execute(() => Materialize().ToSqlQuery().Sql, _source);
   }

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
