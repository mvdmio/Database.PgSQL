using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Linq;

public class QueryRootRewriterTests
{
   private static readonly Expression _rootExpression = Expression.Constant("PROVIDER ROOT");

   [Fact]
   public void Rewrite_WithTheRootDecorator_ReplacesItWithTheProviderRoot()
   {
      var root = CreateRoot();

      var rewritten = QueryRootRewriter.Rewrite(root.Expression, _rootExpression);

      rewritten.Should().BeSameAs(_rootExpression);
   }

   [Fact]
   public void Rewrite_WithADecoratorInsideACall_ReplacesOnlyTheDecorator()
   {
      var root = CreateRoot();
      var call = Expression.Call(typeof(string), nameof(string.Concat), null, Expression.Constant("prefix:"), root.Expression);

      var rewritten = (MethodCallExpression)QueryRootRewriter.Rewrite(call, _rootExpression);

      rewritten.Arguments[0].Should().BeSameAs(call.Arguments[0]);
      rewritten.Arguments[1].Should().BeSameAs(_rootExpression);
   }

   [Fact]
   public void Rewrite_WithAComposedDecorator_UnwrapsUntilItReachesTheRoot()
   {
      var root = CreateRoot();
      var composed = new FakeTranslatedQueryable
      {
         IsRoot = false,
         Expression = Expression.Call(typeof(string), nameof(string.Concat), null, Expression.Constant("composed:"), root.Expression)
      };

      var rewritten = QueryRootRewriter.Rewrite(Expression.Constant(composed), _rootExpression);

      var call = rewritten.Should().BeAssignableTo<MethodCallExpression>().Subject;
      call.Arguments[1].Should().BeSameAs(_rootExpression);
   }

   [Fact]
   public void Rewrite_WithoutAnyDecorator_LeavesTheExpressionAlone()
   {
      var expression = Expression.Constant(42);

      var rewritten = QueryRootRewriter.Rewrite(expression, _rootExpression);

      rewritten.Should().BeSameAs(expression);
   }

   private static FakeTranslatedQueryable CreateRoot()
   {
      var root = new FakeTranslatedQueryable { IsRoot = true };
      root.Expression = Expression.Constant(root);

      return root;
   }

   private sealed class FakeTranslatedQueryable : ITranslatedQueryable
   {
      public bool IsRoot { get; init; }
      public Expression Expression { get; set; } = Expression.Constant(0);
   }
}
