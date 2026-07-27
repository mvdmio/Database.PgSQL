using AwesomeAssertions;
using LinqToDB.Internal.Async;
using mvdmio.Database.PgSQL.Connectors.Linq;
using System.Collections;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Linq;

/// <summary>
///    Guards the decorator's asynchronous paths. If the decorator stops advertising asynchronous execution the
///    provider silently falls back to enumerating synchronously on a pool thread, which no behavioural assertion
///    would notice — so the fake provider underneath refuses to be used synchronously at all.
/// </summary>
public class TranslatedQueryableAsyncTests
{
   [Fact]
   public async Task ToListAsync_MaterializesWithoutEnumeratingSynchronously()
   {
      var query = CreateQuery(["alice", "bob"]);

      var rows = await query.ToListAsync(TestContext.Current.CancellationToken);

      rows.Should().Equal("alice", "bob");
   }

   [Fact]
   public async Task CountAsync_ExecutesThroughTheAsynchronousProvider()
   {
      var query = CreateQuery(["alice", "bob", "carol"]);

      var count = await query.CountAsync(TestContext.Current.CancellationToken);

      count.Should().Be(3);
   }

   [Fact]
   public async Task AnyAsync_ExecutesThroughTheAsynchronousProvider()
   {
      var query = CreateQuery(["alice"]);

      var any = await query.AnyAsync(TestContext.Current.CancellationToken);

      any.Should().BeTrue();
   }

   [Fact]
   public async Task AsyncEnumeration_AfterComposition_StillUsesTheAsynchronousPath()
   {
      var query = CreateQuery(["alice", "bob"]).Where(x => x.Length > 0);

      var rows = await query.ToListAsync(TestContext.Current.CancellationToken);

      rows.Should().HaveCount(2);
   }

   private static IQueryable<string> CreateQuery(List<string> items)
   {
      var root = new AsyncOnlyQueryable<string>(items);

      return new TranslatedQueryable<string>(new LinqQuerySource(() => root, () => null));
   }

   /// <summary>
   ///    Stands in for the provider's queryable: asynchronous execution works, synchronous execution throws.
   /// </summary>
   private sealed class AsyncOnlyQueryable<T> : IQueryable<T>, IAsyncEnumerable<T>, IQueryProviderAsync
   {
      private readonly List<T> _items;

      public AsyncOnlyQueryable(List<T> items)
      {
         _items = items;
      }

      public Type ElementType => typeof(T);
      public Expression Expression => Expression.Constant(this);
      public IQueryProvider Provider => this;

      public IEnumerator<T> GetEnumerator()
      {
         throw new InvalidOperationException("The query was enumerated synchronously.");
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
         return GetEnumerator();
      }

      public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
      {
         return Sequence().GetAsyncEnumerator(cancellationToken);
      }

      public IQueryable CreateQuery(Expression expression)
      {
         return this;
      }

      public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
      {
         return (IQueryable<TElement>)(object)this;
      }

      public object Execute(Expression expression)
      {
         throw new InvalidOperationException("The query was executed synchronously.");
      }

      public TResult Execute<TResult>(Expression expression)
      {
         throw new InvalidOperationException("The query was executed synchronously.");
      }

      public Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken)
      {
         return Task.FromResult((TResult)Convert.ChangeType(_items.Count, typeof(TResult)));
      }

      public Task<IAsyncEnumerable<TResult>> ExecuteAsyncEnumerable<TResult>(Expression expression, CancellationToken cancellationToken)
      {
         return Task.FromResult((IAsyncEnumerable<TResult>)(object)this);
      }

      private async IAsyncEnumerable<T> Sequence()
      {
         foreach (var item in _items)
         {
            await Task.Yield();

            yield return item;
         }
      }
   }
}
