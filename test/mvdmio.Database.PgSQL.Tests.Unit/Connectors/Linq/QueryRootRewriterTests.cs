using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Exceptions;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Linq;

public class QueryRootRewriterTests
{
   private static readonly object _connection = new();

   [Fact]
   public void Rewrite_WithTheRootDecorator_ReplacesItWithTheProviderRoot()
   {
      var (decorator, root) = CreateRoot();

      var rewritten = new QueryRootRewriter().Rewrite(decorator.Expression);

      rewritten.Should().BeSameAs(root.Expression);
   }

   [Fact]
   public void Rewrite_WithADecoratorInsideACall_ReplacesOnlyTheDecorator()
   {
      var (decorator, root) = CreateRoot();
      var call = Expression.Call(typeof(string), nameof(string.Concat), null, Expression.Constant("prefix:"), decorator.Expression);

      var rewritten = (MethodCallExpression)new QueryRootRewriter().Rewrite(call);

      rewritten.Arguments[0].Should().BeSameAs(call.Arguments[0]);
      rewritten.Arguments[1].Should().BeSameAs(root.Expression);
   }

   [Fact]
   public void Rewrite_WithAComposedDecorator_UnwrapsUntilItReachesTheRoot()
   {
      var (decorator, root) = CreateRoot();
      var composed = new FakeTranslatedQueryable
      {
         IsRoot = false,
         Source = decorator.Source,
         Expression = Expression.Call(typeof(string), nameof(string.Concat), null, Expression.Constant("composed:"), decorator.Expression)
      };

      var rewritten = new QueryRootRewriter().Rewrite(Expression.Constant(composed));

      var call = rewritten.Should().BeAssignableTo<MethodCallExpression>().Subject;
      call.Arguments[1].Should().BeSameAs(root.Expression);
   }

   [Fact]
   public void Rewrite_WithoutAnyDecorator_LeavesTheExpressionAlone()
   {
      var expression = Expression.Constant(42);

      var rewritten = new QueryRootRewriter().Rewrite(expression);

      rewritten.Should().BeSameAs(expression);
   }

   /// <remarks>
   ///    A correlated subquery across two generated repositories puts two sources in one expression. Resolving both
   ///    against a single root — which is what this rewriter used to be handed — made the inner query read the outer
   ///    query's table and return the wrong rows without failing.
   /// </remarks>
   [Fact]
   public void Rewrite_WithTwoSourcesOverOneConnection_ResolvesEachAgainstItsOwnRoot()
   {
      var (outer, outerRoot) = CreateRoot();
      var (inner, innerRoot) = CreateRoot();
      var call = Expression.Call(typeof(string), nameof(string.Concat), null, outer.Expression, inner.Expression);

      var rewritten = (MethodCallExpression)new QueryRootRewriter().Rewrite(call);

      rewritten.Arguments[0].Should().BeSameAs(outerRoot.Expression);
      rewritten.Arguments[1].Should().BeSameAs(innerRoot.Expression);
   }

   [Fact]
   public void Rewrite_WithOneSourceNamedTwice_ResolvesItOnce()
   {
      var resolutions = 0;
      var root = Array.Empty<int>().AsQueryable();
      var source = new LinqQuerySource(
         _connection,
         () =>
         {
            resolutions++;
            return root;
         },
         () => null
      );

      var decorator = CreateRootOver(source);
      var call = Expression.Call(typeof(string), nameof(string.Concat), null, decorator.Expression, decorator.Expression);

      new QueryRootRewriter().Rewrite(call);

      resolutions.Should().Be(1);
   }

   [Fact]
   public void Rewrite_WithSourcesFromDifferentConnections_ThrowsQueryTranslationException()
   {
      var (first, _) = CreateRoot();
      var (second, _) = CreateRoot(new object());
      var call = Expression.Call(typeof(string), nameof(string.Concat), null, first.Expression, second.Expression);

      var failure = Record.Exception(() => new QueryRootRewriter().Rewrite(call));

      failure.Should().BeOfType<QueryTranslationException>();
      failure!.Message.Should().Contain("more than one database connection");
   }

   private static (FakeTranslatedQueryable Decorator, IQueryable Root) CreateRoot(object? owner = null)
   {
      var root = Array.Empty<int>().AsQueryable();
      var decorator = CreateRootOver(new LinqQuerySource(owner ?? _connection, () => root, () => null));

      return (decorator, root);
   }

   private static FakeTranslatedQueryable CreateRootOver(LinqQuerySource source)
   {
      var decorator = new FakeTranslatedQueryable { IsRoot = true, Source = source };
      decorator.Expression = Expression.Constant(decorator);

      return decorator;
   }

   private sealed class FakeTranslatedQueryable : ITranslatedQueryable
   {
      public bool IsRoot { get; init; }
      public Expression Expression { get; set; } = Expression.Constant(0);
      public LinqQuerySource Source { get; init; } = null!;
   }
}
