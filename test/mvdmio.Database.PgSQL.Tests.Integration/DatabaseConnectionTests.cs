using AwesomeAssertions;
using AwesomeAssertions.Extensions;
using Dapper;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Npgsql;

namespace mvdmio.Database.PgSQL.Tests.Integration;

public class DatabaseConnectionTests  : TestBase
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
      // Arrange
      await using var notifyConnection = new NpgsqlConnection(_fixture.DbContainer.GetConnectionString());

      // Act
      // Start waiting, but run on separate thread
      var waitTask = Db.WaitAsync("test", CancellationToken);

      // Notify so that the wait task can complete
      await notifyConnection.OpenAsync(CancellationToken);
      await notifyConnection.ExecuteAsync("NOTIFY test", CancellationToken);

      // Assert
      await waitTask;
   }
}
