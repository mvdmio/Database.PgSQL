using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal static class IncludeRewriter
{
   private static readonly MethodInfo _markerDefinition = MarkerDelegate<object>().Method.GetGenericMethodDefinition();

   public static Expression Record<TEntity>(Expression source, IncludeStep step)
   {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(step);

      return Expression.Call(MarkerDelegate<TEntity>().Method, source, Expression.Constant(step));
   }

   public static Expression Rewrite(Expression expression, Func<Expression, ImmutableArray<IncludeStep>, Expression> applySteps)
   {
      ArgumentNullException.ThrowIfNull(expression);
      ArgumentNullException.ThrowIfNull(applySteps);

      var steps = CollectSteps(expression);

      if (steps.IsEmpty)
         return expression;

      return Rebuild(expression, steps, applySteps);
   }

   private static ImmutableArray<IncludeStep> CollectSteps(Expression expression)
   {
      var steps = ImmutableArray.CreateBuilder<IncludeStep>();
      var current = expression;

      while (current is MethodCallExpression call && IsComposedOverAQueryable(call))
      {
         if (TryReadStep(call, out var step))
            steps.Add(step);

         current = call.Arguments[0];
      }

      steps.Reverse();

      return steps.ToImmutable();
   }

   private static Expression Rebuild(Expression expression, ImmutableArray<IncludeStep> steps, Func<Expression, ImmutableArray<IncludeStep>, Expression> applySteps)
   {
      if (expression is not MethodCallExpression call || !IsComposedOverAQueryable(call))
         return applySteps.Invoke(expression, steps);

      var source = Rebuild(call.Arguments[0], steps, applySteps);

      if (TryReadStep(call, out _))
         return source;

      var arguments = call.Arguments.ToArray();
      arguments[0] = source;

      return call.Update(call.Object, arguments);
   }

   private static bool IsComposedOverAQueryable(MethodCallExpression call)
   {
      return call.Object is null
             && call.Arguments.Count > 0
             && typeof(IQueryable).IsAssignableFrom(call.Arguments[0].Type);
   }

   private static bool TryReadStep(MethodCallExpression call, out IncludeStep step)
   {
      if (call.Method.IsGenericMethod
          && call.Method.GetGenericMethodDefinition() == _markerDefinition
          && call.Arguments[1] is ConstantExpression { Value: IncludeStep recorded })
      {
         step = recorded;
         return true;
      }

      step = null!;
      return false;
   }

   private static Func<IQueryable<TEntity>, IncludeStep, IQueryable<TEntity>> MarkerDelegate<TEntity>()
   {
      return Marker;
   }

   private static IQueryable<TEntity> Marker<TEntity>(IQueryable<TEntity> source, IncludeStep step)
   {
      throw new NotSupportedException("A recorded materialization reached execution without being rewritten.");
   }
}
