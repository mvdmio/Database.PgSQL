using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Models;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Integration.Connectors.BulkConnector;

public class BulkConnectorCopyToTempTableTests : TestBase
{
   private readonly TestFixture _fixture;
   private readonly Dictionary<string, Func<TestItem, DbValue>> _columnMapping;

   public BulkConnectorCopyToTempTableTests(TestFixture fixture)
      : base(fixture)
   {
      _fixture = fixture;
      _columnMapping = new Dictionary<string, Func<TestItem, DbValue>>
      {
         { "integer", x => new DbValue(x.Integer, NpgsqlDbType.Integer) },
         { "float", x => new DbValue(x.Float, NpgsqlDbType.Real) },
         { "double", x => new DbValue(x.Double, NpgsqlDbType.Double) },
         { "text", x => new DbValue(x.Text, NpgsqlDbType.Text) },
         { "is_active", x => new DbValue(x.IsActive, NpgsqlDbType.Boolean) },
         { "created_at", x => new DbValue(x.CreatedAt, NpgsqlDbType.TimestampTz) }
      };
   }

   [Fact]
   public async Task CopyToTempTableAsync_WithActiveTransaction_CreatesTempTableAndReturnsGeneratedName()
   {
      var items = TestItem.Create(3);

      var tableName = await Db.Bulk.CopyToTempTableAsync(items, _columnMapping, ct: CancellationToken);
      var count = await Db.Dapper.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName}", ct: CancellationToken);

      tableName.Should().StartWith("temp_");
      count.Should().Be(3);
   }

   [Fact]
   public async Task CopyToTempTableAsync_WithProvidedTableName_UsesProvidedName()
   {
      var items = TestItem.Create(2);

      await Db.Bulk.CopyToTempTableAsync("custom_temp_table", items, _columnMapping, CancellationToken);
      var count = await Db.Dapper.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM custom_temp_table", ct: CancellationToken);

      count.Should().Be(2);
   }

   [Fact]
   public async Task CopyToTempTableAsync_CreatesColumnsBasedOnColumnMapping()
   {
      var tableName = await Db.Bulk.CopyToTempTableAsync(TestItem.Create(1), _columnMapping, ct: CancellationToken);

      var columns = (await Db.Dapper.QueryAsync<TempTableColumn>(
         """
         SELECT
            column_name AS ColumnName,
            data_type AS DataType
         FROM information_schema.columns
         WHERE table_name = :tableName
         ORDER BY ordinal_position
         """,
         new Dictionary<string, object?>
         {
            ["tableName"] = tableName
         },
         ct: CancellationToken
      )).ToArray();

      columns.Should().BeEquivalentTo(
         [
            new TempTableColumn { ColumnName = "integer", DataType = "integer" },
            new TempTableColumn { ColumnName = "float", DataType = "real" },
            new TempTableColumn { ColumnName = "double", DataType = "double precision" },
            new TempTableColumn { ColumnName = "text", DataType = "text" },
            new TempTableColumn { ColumnName = "is_active", DataType = "boolean" },
            new TempTableColumn { ColumnName = "created_at", DataType = "timestamp with time zone" }
         ],
         options => options.WithStrictOrdering()
      );
   }

   [Fact]
   public async Task CopyToTempTableAsync_DropsTableOnCommit()
   {
      await using var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      string tableName = string.Empty;

      await connection.InTransactionAsync(async () =>
      {
         tableName = await connection.Bulk.CopyToTempTableAsync(TestItem.Create(2), _columnMapping, ct: CancellationToken);

         var count = await connection.Dapper.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName}", ct: CancellationToken);
         count.Should().Be(2);
      });

      Func<Task> action = async () => await connection.Dapper.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName}", ct: CancellationToken);

      await action.Should().ThrowAsync<QueryException>();
   }

   private sealed record TempTableColumn
   {
      public required string ColumnName { get; init; }
      public required string DataType { get; init; }
   }

   private sealed record TestItem
   {
      public required int Integer { get; init; }
      public required float Float { get; init; }
      public required double Double { get; init; }
      public required string Text { get; init; }
      public required bool IsActive { get; init; }
      public required DateTime CreatedAt { get; init; }

      public static TestItem[] Create(int count)
      {
         return Enumerable.Range(1, count)
            .Select(x => new TestItem
            {
               Integer = x,
               Float = x * 1.1f,
               Double = x * 1.1d,
               Text = $"Test {x}",
               IsActive = x % 2 == 0,
               CreatedAt = new DateTime(2026, 1, x, 10, 0, 0, DateTimeKind.Utc)
            })
            .ToArray();
      }
   }
}
