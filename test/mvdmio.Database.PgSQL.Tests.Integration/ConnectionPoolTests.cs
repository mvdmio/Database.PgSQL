using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Npgsql;
using System.Diagnostics.Metrics;

namespace mvdmio.Database.PgSQL.Tests.Integration;

/// <summary>
///    That every data source the library builds caps its pool and names it after the entry assembly, unless a connection
///    string keyword says otherwise, as Postgres and Npgsql show it from outside.
/// </summary>
/// <remarks>
///    Not derived from <c>TestBase</c>: the point is the data source each test builds itself. Under xUnit v3 the test
///    project runs as its own executable, so the entry assembly is this test assembly. Each exhaustion test uses a
///    <c>Timeout=1</c> keyword so the 11th open gives up after a second instead of Npgsql's default 15.
/// </remarks>
public class ConnectionPoolTests
{
   private const string ENTRY_ASSEMBLY_NAME = "mvdmio.Database.PgSQL.Tests.Integration";

   private readonly TestFixture _fixture;

   public ConnectionPoolTests(TestFixture fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   [Fact]
   public async Task ConnectionFromTheFactory_ShowsTheEntryAssemblyNameInPgStatActivity()
   {
      await using var factory = new DatabaseConnectionFactory();
      await using var db = factory.BuildConnection(_fixture.DbContainer.GetConnectionString());

      (await ReadOwnApplicationNameAsync(db)).Should().Be(ENTRY_ASSEMBLY_NAME);
   }

   [Fact]
   public async Task DataSourceFromTheFactory_WithNoKeywords_IsExhaustedAfterTenConnections()
   {
      await using var factory = new DatabaseConnectionFactory();
      var dataSource = factory.BuildDataSource($"{_fixture.DbContainer.GetConnectionString()};Timeout=1");

      await AssertExhaustedAtAsync(dataSource, 10);
   }

   [Fact]
   public async Task DataSourceFromTheFactory_WithCapAndNameKeywords_KeywordsBeatTheDefaults()
   {
      var name = $"pool-keyword-{Guid.NewGuid():N}";
      await using var factory = new DatabaseConnectionFactory();
      var connectionString = $"{_fixture.DbContainer.GetConnectionString()};Timeout=1;Maximum Pool Size=3;Application Name={name}";

      await using var db = factory.BuildConnection(connectionString);
      (await ReadOwnApplicationNameAsync(db)).Should().Be(name);

      await AssertExhaustedAtAsync(factory.BuildDataSource(connectionString), 3);
   }

   [Fact]
   public async Task DirectlyConstructedConnection_GetsTheDefaultNameAndCap()
   {
      await using var db = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

      await db.OpenAsync(CancellationToken);
      var reported = new NpgsqlConnectionStringBuilder(db.Connection!.ConnectionString);
      var applicationName = await ReadOwnApplicationNameAsync(db);
      await db.CloseAsync(CancellationToken);

      applicationName.Should().Be(ENTRY_ASSEMBLY_NAME);
      reported.MaxPoolSize.Should().Be(10);
   }

   [Fact]
   public async Task DataSourceFromTheFactory_DiagnosticNameEqualsThePoolName()
   {
      var name = $"pool-metrics-{Guid.NewGuid():N}";
      await using var factory = new DatabaseConnectionFactory();
      await using var db = factory.BuildConnection($"{_fixture.DbContainer.GetConnectionString()};Application Name={name}");

      await db.OpenAsync(CancellationToken);
      var maxByPool = ObserveMaxConnectionsByPoolName();
      await db.CloseAsync(CancellationToken);

      maxByPool.Should().ContainKey(name).WhoseValue.Should().Be(10);
   }

   [Fact]
   public async Task DirectlyConstructedConnection_DiagnosticNameEqualsThePoolName()
   {
      var name = $"pool-direct-metrics-{Guid.NewGuid():N}";
      await using var db = new DatabaseConnection($"{_fixture.DbContainer.GetConnectionString()};Application Name={name}");

      await db.OpenAsync(CancellationToken);
      var maxByPool = ObserveMaxConnectionsByPoolName();
      await db.CloseAsync(CancellationToken);

      maxByPool.Should().ContainKey(name).WhoseValue.Should().Be(10);
   }

   private static async Task<string> ReadOwnApplicationNameAsync(DatabaseConnection db)
   {
      return await db.Dapper.QuerySingleAsync<string>(
         "SELECT application_name FROM pg_stat_activity WHERE pid = pg_backend_pid()",
         ct: CancellationToken
      );
   }

   private static async Task AssertExhaustedAtAsync(NpgsqlDataSource dataSource, int cap)
   {
      var held = new List<NpgsqlConnection>();

      try
      {
         for (var i = 0; i < cap; i++)
            held.Add(await dataSource.OpenConnectionAsync(CancellationToken));

         var openOneMore = async () => held.Add(await dataSource.OpenConnectionAsync(CancellationToken));

         await openOneMore.Should().ThrowAsync<NpgsqlException>().WithMessage("*pool has been exhausted*");
      }
      finally
      {
         foreach (var connection in held)
            await connection.DisposeAsync();
      }
   }

   // Npgsql publishes each pool's cap as db.client.connection.max, tagged with the data source's diagnostic name.
   private static Dictionary<string, long> ObserveMaxConnectionsByPoolName()
   {
      var maxByPool = new Dictionary<string, long>();

      using var listener = new MeterListener();
      listener.InstrumentPublished = (instrument, l) =>
      {
         if (instrument.Meter.Name == "Npgsql" && instrument.Name == "db.client.connection.max")
            l.EnableMeasurementEvents(instrument);
      };
      listener.SetMeasurementEventCallback<int>((_, value, tags, _) => Record(maxByPool, value, tags));
      listener.SetMeasurementEventCallback<long>((_, value, tags, _) => Record(maxByPool, value, tags));
      listener.Start();
      listener.RecordObservableInstruments();

      return maxByPool;
   }

   private static void Record(Dictionary<string, long> maxByPool, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
   {
      foreach (var tag in tags)
      {
         if (tag is { Key: "db.client.connection.pool.name", Value: string poolName })
            maxByPool[poolName] = value;
      }
   }
}
