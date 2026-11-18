using Dapper;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Npgsql;

namespace mvdmio.Database.PgSQL.Tests.Integration;

public class DatabaseConnectionTests : TestBase
{
   private readonly TestFixture _fixture;

   public DatabaseConnectionTests(TestFixture fixture)
      : base(fixture)
   {
      _fixture = fixture;
   }

   [Fact]
   public async Task WaitAsync_ShouldWaitForChannelNotification()
   {
      await using var notifyConnection = new NpgsqlConnection(_fixture.DbContainer.GetConnectionString());

      var waitTask = Db.WaitAsync("test", CancellationToken);

      await notifyConnection.OpenAsync(CancellationToken);
      await notifyConnection.ExecuteAsync("NOTIFY test", CancellationToken);

      await waitTask;
   }
}
