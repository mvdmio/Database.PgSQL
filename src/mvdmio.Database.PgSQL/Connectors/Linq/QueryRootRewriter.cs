using mvdmio.Database.PgSQL.Exceptions;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Replaces every reference to a query decorator inside an expression tree with the provider's own root
///    expression. Without this the provider would find a foreign type at the root of every query it is handed.
/// </summary>
/// <remarks>
///    Each decorator resolves against the source it was composed over rather than against a single root handed in for
///    the whole tree. One expression can name more than one source — a correlated subquery across two generated
///    repositories does — and resolving them all onto one root would make the inner query read the outer query's
///    table. Sources that cannot belong to one query are rejected instead of producing wrong rows.
/// </remarks>
internal sealed class QueryRootRewriter : ExpressionVisitor
{
   private readonly Dictionary<LinqQuerySource, IQueryable> _roots = [];
   private object? _owner;

   /// <summary>
   ///    Resolves the provider's root for <paramref name="source" />, reusing the one already resolved for it during
   ///    this rewrite so a source named twice still resolves to a single table.
   /// </summary>
   /// <param name="source">The source to resolve.</param>
   /// <returns>The provider's queryable for the source's table.</returns>
   /// <exception cref="QueryTranslationException">
   ///    Thrown when the source belongs to a different query surface than the ones already resolved, which means the
   ///    expression combines connections and cannot be one query.
   /// </exception>
   public IQueryable ResolveRoot(LinqQuerySource source)
   {
      ArgumentNullException.ThrowIfNull(source);

      if (_roots.TryGetValue(source, out var resolved))
         return resolved;

      if (_owner is not null && !ReferenceEquals(_owner, source.Owner))
      {
         throw new QueryTranslationException(
            new NotSupportedException("The query combines queryables from more than one database connection, which cannot execute as a single statement.")
         );
      }

      var root = source.ResolveRoot();

      _owner = source.Owner;
      _roots.Add(source, root);

      return root;
   }

   /// <summary>
   ///    Rewrites <paramref name="expression" />, substituting each decorator it finds for the root of that
   ///    decorator's own source.
   /// </summary>
   /// <param name="expression">The expression to rewrite.</param>
   /// <returns>The rewritten expression.</returns>
   public Expression Rewrite(Expression expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      return Visit(expression);
   }

   /// <summary>
   ///    Rewrites a lambda, keeping its type so it can go straight back to whatever expects that lambda.
   /// </summary>
   /// <typeparam name="TDelegate">The lambda's delegate type.</typeparam>
   /// <param name="expression">The lambda to rewrite.</param>
   /// <returns>The rewritten lambda.</returns>
   public Expression<TDelegate> Rewrite<TDelegate>(Expression<TDelegate> expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      return (Expression<TDelegate>)Visit(expression);
   }

   /// <inheritdoc />
   protected override Expression VisitConstant(ConstantExpression node)
   {
      if (node.Value is not ITranslatedQueryable translated)
         return base.VisitConstant(node);

      // A composed decorator's own expression still holds the root decorator, so keep unwrapping.
      return translated.IsRoot ? ResolveRoot(translated.Source).Expression : Visit(translated.Expression);
   }
}
