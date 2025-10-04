using AwesomeAssertions;
using mvdmio.Database.PgSQL.Models;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Integration.Connectors.BulkConnector;

public class BulkConnectorInsertOrUpdateTests : TestBase
{
   private readonly Dictionary<string, Func<TestItem, DbValue>> _columnMapping;

   public BulkConnectorInsertOrUpdateTests(TestFixture fixture)
      : base(fixture)
   {
      _columnMapping = new Dictionary<string, Func<TestItem, DbValue>> {
         { "integer", x => new DbValue(x.Integer, NpgsqlDbType.Integer) },
         { "float", x => new DbValue(x.Float, NpgsqlDbType.Real) },
         { "double", x => new DbValue(x.Double, NpgsqlDbType.Double) },
         { "text", x => new DbValue(x.Text, NpgsqlDbType.Text) }
      };
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS test_upsert (
            id              bigint           NOT NULL GENERATED ALWAYS AS IDENTITY,
            integer         integer          NOT NULL,
            float           real             NOT NULL,
            double          double precision NOT NULL,
            text            text             NOT NULL,
            created_at      timestamptz DEFAULT CURRENT_TIMESTAMP NOT NULL,
            last_updated_at timestamptz DEFAULT CURRENT_TIMESTAMP NOT NULL,
            PRIMARY KEY (integer)
         );
         """
      );
   }

   [Fact]
   public async Task WithEmptyTable()
   {
      // Arrange
      var items = TestItem.Create(10);

      // Act
      await Db.Bulk.InsertOrUpdateAsync("test_upsert", [ "integer" ], items, _columnMapping, CancellationToken);

      // Assert
      var result = (await Db.Dapper.QueryAsync<TestItem>(
         "SELECT * FROM test_upsert ORDER BY integer"
      )).ToArray();

      result.Should().HaveCount(10);
      result[0].Text.Should().Be("Test 1");
      result[1].Text.Should().Be("Test 2");
      result[2].Text.Should().Be("Test 3");
      result[3].Text.Should().Be("Test 4");
      result[4].Text.Should().Be("Test 5");
      result[5].Text.Should().Be("Test 6");
      result[6].Text.Should().Be("Test 7");
      result[7].Text.Should().Be("Test 8");
      result[8].Text.Should().Be("Test 9");
      result[9].Text.Should().Be("Test 10");
   }

   [Fact]
   public async Task WithExistingValues()
   {
      // Arrange
      var items = TestItem.Create(10);
      var updateItems = items.Select(x => new TestItem {
            Integer = x.Integer,
            Float = x.Float * 2,
            Double = x.Double * 2,
            Text = x.Text + " Updated"
         }
      );

      // Act
      await Db.Bulk.CopyAsync("test_upsert", items, _columnMapping, CancellationToken);
      await Db.Bulk.InsertOrUpdateAsync("test_upsert", [ "integer" ], updateItems, _columnMapping, CancellationToken);

      // Assert
      var result = (await Db.Dapper.QueryAsync<TestItem>(
         "SELECT * FROM test_upsert ORDER BY integer"
      )).ToArray();

      result.Should().HaveCount(10);
      result[0].Text.Should().Be("Test 1 Updated");
      result[1].Text.Should().Be("Test 2 Updated");
      result[2].Text.Should().Be("Test 3 Updated");
      result[3].Text.Should().Be("Test 4 Updated");
      result[4].Text.Should().Be("Test 5 Updated");
      result[5].Text.Should().Be("Test 6 Updated");
      result[6].Text.Should().Be("Test 7 Updated");
      result[7].Text.Should().Be("Test 8 Updated");
      result[8].Text.Should().Be("Test 9 Updated");
      result[9].Text.Should().Be("Test 10 Updated");
   }

   private sealed record TestItem
   {
      public required int Integer { get; init; }
      public required float Float { get; init; }
      public required double Double { get; init; }
      public required string Text { get; init; }

      public static TestItem[] Create(int count)
      {
         return Enumerable.Range(1, count)
            .Select(x => new TestItem {
               Integer = x,
               Float = x * 1.1f,
               Double = x * 1.1d,
               Text = $"Test {x}"
            })
            .ToArray();
      }
   }
}
