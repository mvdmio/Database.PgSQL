using AwesomeAssertions;
using mvdmio.Database.PgSQL.Models;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Integration.Connectors.BulkConnector;

public class BulkConnectorBeginCopyTests : TestBase
{
   public BulkConnectorBeginCopyTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS test_begin_copy (
            integer integer NOT NULL PRIMARY KEY,
            text    text    NOT NULL
         );
         """
      );
   }

   [Fact]
   public async Task WriteAsync_WhenMappingFails_ReleasesCopyStateForNextOperation()
   {
      var failingMapping = new Dictionary<string, Func<TestItem, DbValue>>
      {
         { "integer", x => new DbValue(x.Integer, NpgsqlDbType.Integer) },
         { "text", _ => throw new InvalidOperationException("Simulated streaming mapping failure") }
      };

      await using (var session = await Db.Bulk.BeginCopyAsync("test_begin_copy", failingMapping, CancellationToken))
      {
         Func<Task> action = async () => await session.WriteAsync(
            new TestItem { Integer = 1, Text = "Test 1" },
            CancellationToken
         );

         var exception = (await action.Should().ThrowAsync<InvalidOperationException>()).Which;
         exception.Message.Should().Contain("Simulated streaming mapping failure");
      }

      await Db.RollbackTransactionAsync(CancellationToken);
      await Db.BeginTransactionAsync(ct: CancellationToken);

      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS test_begin_copy (
            integer integer NOT NULL PRIMARY KEY,
            text    text    NOT NULL
         );
         """
      );

      var validMapping = new Dictionary<string, Func<TestItem, DbValue>>
      {
         { "integer", x => new DbValue(x.Integer, NpgsqlDbType.Integer) },
         { "text", x => new DbValue(x.Text, NpgsqlDbType.Text) }
      };

      await Db.Bulk.CopyAsync(
         "test_begin_copy",
         [new TestItem { Integer = 2, Text = "Recovered" }],
         validMapping,
         CancellationToken
      );

      var rows = (await Db.Dapper.QueryAsync<TestItem>("SELECT * FROM test_begin_copy", ct: CancellationToken)).ToArray();
      rows.Should().ContainSingle();
      rows[0].Text.Should().Be("Recovered");
   }

   private sealed record TestItem
   {
      public required int Integer { get; init; }
      public required string Text { get; init; }
   }
}
