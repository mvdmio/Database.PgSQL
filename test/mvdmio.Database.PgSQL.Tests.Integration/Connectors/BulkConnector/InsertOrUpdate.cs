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
      _columnMapping = new Dictionary<string, Func<TestItem, DbValue>>
      {
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
      var result = (await Db.Bulk.InsertOrUpdateAsync(
         "test_upsert",
         ["integer"],
         items,
         _columnMapping,
         CancellationToken
      )).ToArray();

      // Assert
      result.Should().HaveCount(10);
      result.Where(x => x.IsUpdated).Should().HaveCount(0);
      result.Where(x => x.IsInserted).Should().HaveCount(10);

      var rows = (await Db.Dapper.QueryAsync<TestItem>("SELECT * FROM test_upsert ORDER BY integer", ct: CancellationToken)).ToArray();

      rows.Should().HaveCount(10);
      rows[0].Text.Should().Be("Test 1");
      rows[1].Text.Should().Be("Test 2");
      rows[2].Text.Should().Be("Test 3");
      rows[3].Text.Should().Be("Test 4");
      rows[4].Text.Should().Be("Test 5");
      rows[5].Text.Should().Be("Test 6");
      rows[6].Text.Should().Be("Test 7");
      rows[7].Text.Should().Be("Test 8");
      rows[8].Text.Should().Be("Test 9");
      rows[9].Text.Should().Be("Test 10");
   }

   [Fact]
   public async Task WithChangedValues()
   {
      // Arrange
      var items = TestItem.Create(10);

      var updateItems = items.Select(x => new TestItem
         {
            Integer = x.Integer,
            Float = x.Float   * 2,
            Double = x.Double * 2,
            Text = x.Text + " Updated"
         }
      );

      // Act
      await Db.Bulk.CopyAsync(
         "test_upsert",
         items,
         _columnMapping,
         CancellationToken
      );

      var result = (await Db.Bulk.InsertOrUpdateAsync(
         "test_upsert",
         ["integer"],
         updateItems,
         _columnMapping,
         CancellationToken
      )).ToArray();

      // Assert
      result.Should().HaveCount(10);
      result.Where(x => x.IsUpdated).Should().HaveCount(10);
      result.Where(x => x.IsInserted).Should().HaveCount(0);

      var rows = (await Db.Dapper.QueryAsync<TestItem>("SELECT * FROM test_upsert ORDER BY integer", ct: CancellationToken)).ToArray();

      rows.Should().HaveCount(10);
      rows[0].Text.Should().Be("Test 1 Updated");
      rows[1].Text.Should().Be("Test 2 Updated");
      rows[2].Text.Should().Be("Test 3 Updated");
      rows[3].Text.Should().Be("Test 4 Updated");
      rows[4].Text.Should().Be("Test 5 Updated");
      rows[5].Text.Should().Be("Test 6 Updated");
      rows[6].Text.Should().Be("Test 7 Updated");
      rows[7].Text.Should().Be("Test 8 Updated");
      rows[8].Text.Should().Be("Test 9 Updated");
      rows[9].Text.Should().Be("Test 10 Updated");
   }

   [Fact]
   public async Task WithUnchangedValues()
   {
      // Arrange
      var items = TestItem.Create(10);

      var updateItems = items.Select(x => new TestItem
         {
            Integer = x.Integer,
            Float = x.Float,
            Double = x.Double,
            Text = x.Text
         }
      );

      // Act
      await Db.Bulk.CopyAsync(
         "test_upsert",
         items,
         _columnMapping,
         CancellationToken
      );

      var result = (await Db.Bulk.InsertOrUpdateAsync(
         "test_upsert",
         ["integer"],
         updateItems,
         _columnMapping,
         CancellationToken
      )).ToArray();

      // Assert
      result.Should().HaveCount(0);
      result.Where(x => x.IsUpdated).Should().HaveCount(0);
      result.Where(x => x.IsInserted).Should().HaveCount(0);

      var rows = (await Db.Dapper.QueryAsync<TestItem>("SELECT * FROM test_upsert ORDER BY integer", ct: CancellationToken)).ToArray();

      rows.Should().HaveCount(10);
      rows[0].Text.Should().Be("Test 1");
      rows[1].Text.Should().Be("Test 2");
      rows[2].Text.Should().Be("Test 3");
      rows[3].Text.Should().Be("Test 4");
      rows[4].Text.Should().Be("Test 5");
      rows[5].Text.Should().Be("Test 6");
      rows[6].Text.Should().Be("Test 7");
      rows[7].Text.Should().Be("Test 8");
      rows[8].Text.Should().Be("Test 9");
      rows[9].Text.Should().Be("Test 10");
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
            .Select(x => new TestItem
               {
                  Integer = x,
                  Float = x  * 1.1f,
                  Double = x * 1.1d,
                  Text = $"Test {x}"
               }
            )
            .ToArray();
      }
   }
}
