using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Records materialization requests as nodes of this library's own inside a composed expression, and replaces them
///    at execution time with the provider's eager-loading calls against the resolved root.
/// </summary>
/// <remarks>
///    <para>
///       The provider's operator cannot be called while a query is being composed: it ends by casting what the
///       source's own provider returns back to a provider-internal query type, which this library's decorator is not.
///       The documented workaround — handing the decorator's provider through to the provider's own — would bind the
///       query to a connection and transaction at composition time and drop the decorator from the chain, which would
///       break composing before a transaction begins and silently disable exception translation, SQL diagnostics and
///       the disposed-connection error.
///    </para>
///    <para>
///       Replaying at execution time keeps all of those, and lifts a restriction as well: the provider requires the
///       second level of a chain to immediately follow the first, because its own chaining marker does not survive an
///       intervening operator. The replayed calls are emitted contiguously, so in consumer code they need not be.
///    </para>
/// </remarks>
internal static class IncludeRewriter
{
   private static readonly MethodInfo _markerDefinition = MarkerDelegate<object>().Method.GetGenericMethodDefinition();

   /// <summary>
   ///    Records <paramref name="step" /> on top of the composition <paramref name="source" /> describes.
   /// </summary>
   /// <typeparam name="TEntity">The element type of the query the step is recorded on.</typeparam>
   /// <param name="source">The expression of the composition the step applies to.</param>
   /// <param name="step">The step to record.</param>
   /// <returns>An expression of the same element type, carrying the step.</returns>
   public static Expression Record<TEntity>(Expression source, IncludeStep step)
   {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(step);

      return Expression.Call(MarkerDelegate<TEntity>().Method, source, Expression.Constant(step));
   }

   /// <summary>
   ///    Strips the recorded nodes from <paramref name="expression" /> and hands the composition's innermost source,
   ///    together with the steps in the order they were composed, to <paramref name="applySteps" />.
   /// </summary>
   /// <param name="expression">The composed expression to rewrite.</param>
   /// <param name="applySteps">
   ///    Produces the replacement for the innermost source. Receives that source and every recorded step, and is not
   ///    called at all when the expression records none.
   /// </param>
   /// <returns>The expression with the recorded nodes replaced.</returns>
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

   /// <remarks>
   ///    Never runs. It exists so a recorded step can travel through composition as an ordinary call node of the right
   ///    element type; throwing rather than returning makes a node that escaped the rewrite loud instead of silent.
   /// </remarks>
   private static IQueryable<TEntity> Marker<TEntity>(IQueryable<TEntity> source, IncludeStep step)
   {
      throw new NotSupportedException("A recorded materialization reached execution without being rewritten.");
   }
}
