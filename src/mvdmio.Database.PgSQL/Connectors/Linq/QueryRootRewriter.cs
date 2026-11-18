using mvdmio.Database.PgSQL.Exceptions;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal sealed class QueryRootRewriter : ExpressionVisitor
{
   private readonly Dictionary<LinqQuerySource, IQueryable> _roots = [];
   private object? _owner;

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

   public Expression Rewrite(Expression expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      return Visit(expression);
   }

   public Expression<TDelegate> Rewrite<TDelegate>(Expression<TDelegate> expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      return (Expression<TDelegate>)Visit(expression);
   }

   protected override Expression VisitConstant(ConstantExpression node)
   {
      if (node.Value is not ITranslatedQueryable translated)
         return base.VisitConstant(node);

      return translated.IsRoot ? ResolveRoot(translated.Source).Expression : Visit(translated.Expression);
   }
}
