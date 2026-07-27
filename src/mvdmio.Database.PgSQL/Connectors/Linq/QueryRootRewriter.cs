using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Replaces every reference to a query decorator inside an expression tree with the provider's own root
///    expression. Without this the provider would find a foreign type at the root of every query it is handed.
/// </summary>
internal sealed class QueryRootRewriter : ExpressionVisitor
{
   private readonly Expression _rootExpression;

   private QueryRootRewriter(Expression rootExpression)
   {
      _rootExpression = rootExpression;
   }

   /// <summary>
   ///    Rewrites <paramref name="expression" />, substituting <paramref name="rootExpression" /> for the decorator
   ///    at the root of the composition.
   /// </summary>
   public static Expression Rewrite(Expression expression, Expression rootExpression)
   {
      ArgumentNullException.ThrowIfNull(expression);
      ArgumentNullException.ThrowIfNull(rootExpression);

      return new QueryRootRewriter(rootExpression).Visit(expression);
   }

   /// <inheritdoc />
   protected override Expression VisitConstant(ConstantExpression node)
   {
      if (node.Value is not ITranslatedQueryable translated)
         return base.VisitConstant(node);

      // A composed decorator's own expression still holds the root decorator, so keep unwrapping.
      return translated.IsRoot ? _rootExpression : Visit(translated.Expression);
   }
}
