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

      using var deadline = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      deadline.CancelAfter(TimeSpan.FromSeconds(60));
      var token = deadline.Token;

      var waitTask = Db.WaitAsync("test", token);

      await notifyConnection.OpenAsync(token);

      while (!waitTask.IsCompleted)
      {
         await notifyConnection.ExecuteAsync(new CommandDefinition("NOTIFY test", cancellationToken: token));
         await Task.Delay(100, token);
      }

      await waitTask;
   }
}
