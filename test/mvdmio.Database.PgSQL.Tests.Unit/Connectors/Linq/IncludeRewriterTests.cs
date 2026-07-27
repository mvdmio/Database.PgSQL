using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using System.Collections.Immutable;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Linq;

/// <summary>
///    The rewriter's contract is expression in, expression out: which steps it found, in what order, and what it left of
///    the rest of the composition. What a step then does to the provider's query is captured in the step's own delegate,
///    so what distinguishes one kind of materialization from another is invisible here — which operator records which
///    step belongs to <see cref="QueryableExtensionsTests" />, and what the provider makes of it, to the integration
///    tests.
/// </summary>
public class IncludeRewriterTests
{
   private static readonly IQueryable<int> _source = Array.Empty<int>().AsQueryable();
   private static readonly Expression _innermost = Expression.Constant(_source);
   private static readonly Expression _replacement = Expression.Constant(Array.Empty<int>().AsQueryable());

   [Fact]
   public void Rewrite_WithoutAnyStep_LeavesTheExpressionAlone()
   {
      var expression = Operator(_innermost, "only");

      var rewritten = IncludeRewriter.Rewrite(expression, (_, _) => throw new InvalidOperationException("The innermost source must not be replaced when nothing was recorded."));

      rewritten.Should().BeSameAs(expression);
   }

   [Fact]
   public void Rewrite_WithASingleStep_StripsItAndHandsOverTheInnermostSource()
   {
      var step = new IncludeStep((queryable, _) => queryable);
      var expression = IncludeRewriter.Record<int>(_innermost, step);

      var rewritten = Rewrite(expression, out var innermost, out var steps);

      rewritten.Should().BeSameAs(_replacement);
      innermost.Should().BeSameAs(_innermost);
      steps.Should().Equal(step);
   }

   [Fact]
   public void Rewrite_WithAChainedStep_HandsOverBothInCompositionOrder()
   {
      var first = new IncludeStep((queryable, _) => queryable);
      var second = new IncludeStep((queryable, _) => queryable);
      var expression = IncludeRewriter.Record<int>(IncludeRewriter.Record<int>(_innermost, first), second);

      var rewritten = Rewrite(expression, out _, out var steps);

      rewritten.Should().BeSameAs(_replacement);
      steps.Should().Equal(first, second);
   }

   [Fact]
   public void Rewrite_WithAnOperatorBetweenTwoSteps_KeepsTheOperatorAndHandsOverBothSteps()
   {
      var first = new IncludeStep((queryable, _) => queryable);
      var second = new IncludeStep((queryable, _) => queryable);
      var expression = IncludeRewriter.Record<int>(
         Operator(IncludeRewriter.Record<int>(_innermost, first), "between"),
         second
      );

      var rewritten = Rewrite(expression, out _, out var steps);

      var call = rewritten.Should().BeAssignableTo<MethodCallExpression>().Subject;
      call.Method.Name.Should().Be(nameof(Operator));
      call.Arguments[0].Should().BeSameAs(_replacement);
      ((ConstantExpression)call.Arguments[1]).Value.Should().Be("between");
      steps.Should().Equal(first, second);
   }

   [Fact]
   public void Rewrite_WithAnOperatorAroundTheWholeComposition_RebuildsItOverTheReplacement()
   {
      var step = new IncludeStep((queryable, _) => queryable);
      var expression = Operator(IncludeRewriter.Record<int>(_innermost, step), "outer");

      var rewritten = Rewrite(expression, out _, out var steps);

      var call = rewritten.Should().BeAssignableTo<MethodCallExpression>().Subject;
      call.Arguments[0].Should().BeSameAs(_replacement);
      steps.Should().Equal(step);
   }

   private static Expression Rewrite(Expression expression, out Expression? innermost, out ImmutableArray<IncludeStep> steps)
   {
      Expression? seenInnermost = null;
      var seenSteps = ImmutableArray<IncludeStep>.Empty;

      var rewritten = IncludeRewriter.Rewrite(
         expression,
         (source, recorded) =>
         {
            seenInnermost = source;
            seenSteps = recorded;

            return _replacement;
         }
      );

      innermost = seenInnermost;
      steps = seenSteps;

      return rewritten;
   }

   private static Expression Operator(Expression source, string tag)
   {
      Func<IQueryable<int>, string, IQueryable<int>> composed = Operator;

      return Expression.Call(composed.Method, source, Expression.Constant(tag));
   }

   /// <remarks>Stands in for any operator that composes over a queryable without changing its element type.</remarks>
   private static IQueryable<int> Operator(IQueryable<int> source, string tag)
   {
      return source;
   }
}
