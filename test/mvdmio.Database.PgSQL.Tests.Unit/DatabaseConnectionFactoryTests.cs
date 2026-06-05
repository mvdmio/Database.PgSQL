using AwesomeAssertions;
using Npgsql;

namespace mvdmio.Database.PgSQL.Tests.Unit;

/// <summary>
/// Tests for the thread-safety and caching behavior of <see cref="DatabaseConnectionFactory"/>.
/// These tests do not require a live database: <see cref="NpgsqlDataSourceBuilder.Build"/> parses the
/// connection string but does not open a connection.
/// </summary>
public class DatabaseConnectionFactoryTests
{
   private const string CONNECTION_STRING = "Host=localhost;Database=test;Username=test;Password=test";

   [Fact]
   public async Task BuildDataSource_SameConnectionStringUnderContention_BuildsOnceAndReturnsSameInstance()
   {
      const int CONCURRENCY = 64;

      using var factory = new DatabaseConnectionFactory();

      var builderInvocations = 0;
      void BuilderAction(NpgsqlDataSourceBuilder _) => Interlocked.Increment(ref builderInvocations);

      // Release all tasks simultaneously to maximise the chance of hitting the creation race.
      var startSignal = new TaskCompletionSource();

      var tasks = Enumerable.Range(0, CONCURRENCY).Select(_ => Task.Run(async () =>
      {
         await startSignal.Task;
         return factory.BuildDataSource(CONNECTION_STRING, BuilderAction);
      })).ToList();

      startSignal.SetResult();

      var dataSources = await Task.WhenAll(tasks);

      // The builder action only runs when the data source is actually built. With Lazy this is exactly once,
      // even when GetOrAdd races several wrapper creations.
      builderInvocations.Should().Be(1);

      // Every caller must observe the single cached instance.
      dataSources.Should().OnlyContain(x => ReferenceEquals(x, dataSources[0]));
   }

   [Fact]
   public void BuildDataSource_DistinctConnectionStrings_ReturnsDistinctInstances()
   {
      using var factory = new DatabaseConnectionFactory();

      var first = factory.BuildDataSource("Host=localhost;Database=one;Username=test;Password=test");
      var second = factory.BuildDataSource("Host=localhost;Database=two;Username=test;Password=test");

      first.Should().NotBeSameAs(second);
   }

   [Fact]
   public void BuildDataSource_SameConnectionString_ReturnsCachedInstance()
   {
      using var factory = new DatabaseConnectionFactory();

      var first = factory.BuildDataSource(CONNECTION_STRING);
      var second = factory.BuildDataSource(CONNECTION_STRING);

      first.Should().BeSameAs(second);
   }

   [Fact]
   public void BuildDataSource_AfterDispose_ThrowsObjectDisposedException()
   {
      var factory = new DatabaseConnectionFactory();
      factory.Dispose();

      var act = () => factory.BuildDataSource(CONNECTION_STRING);

      act.Should().Throw<ObjectDisposedException>();
   }
}
