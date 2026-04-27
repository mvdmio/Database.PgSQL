using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Npgsql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Connectors.BulkConnector;

/// <summary>
///    Integration tests for <see cref="PgSQL.Connectors.Bulk.BulkConnector.CopyFromAsync"/>.
///    Uses two databases on the shared PostgreSQL container to simulate cross-database copy.
/// </summary>
public class CopyFromTests : IAsyncLifetime
{
   private readonly TestFixture _fixture;
   private string _sourceDbName = null!;
   private string _destDbName = null!;
   private string _sourceConnectionString = null!;
   private string _destConnectionString = null!;

   protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public CopyFromTests(TestFixture fixture)
   {
      _fixture = fixture;
   }

   public async ValueTask InitializeAsync()
   {
      var unique = Guid.NewGuid().ToString("N")[..12];
      _sourceDbName = $"copy_src_{unique}";
      _destDbName = $"copy_dst_{unique}";

      _sourceConnectionString = await CreateDatabaseAsync(_sourceDbName);
      _destConnectionString = await CreateDatabaseAsync(_destDbName);
   }

   public async ValueTask DisposeAsync()
   {
      await DropDatabaseAsync(_sourceDbName);
      await DropDatabaseAsync(_destDbName);
   }

   [Fact]
   public async Task CopyFromAsync_StreamsAllRowsToDestination()
   {
      await using var source = new DatabaseConnection(_sourceConnectionString);
      await using var dest = new DatabaseConnection(_destConnectionString);

      const string createTable = """
         CREATE TABLE public.widgets (
            id           bigint NOT NULL PRIMARY KEY,
            name         text   NOT NULL,
            payload      jsonb,
            created_at   timestamptz NOT NULL,
            tags         text[]
         );
         """;

      await source.Dapper.ExecuteAsync(createTable, ct: CancellationToken);
      await dest.Dapper.ExecuteAsync(createTable, ct: CancellationToken);

      await source.Dapper.ExecuteAsync(
         """
         INSERT INTO public.widgets (id, name, payload, created_at, tags) VALUES
            (1, 'alpha', '{"k":1}'::jsonb, '2026-01-01T00:00:00Z'::timestamptz, ARRAY['x','y']),
            (2, 'beta',  NULL,             '2026-02-01T00:00:00Z'::timestamptz, NULL),
            (3, 'gamma', '[1,2,3]'::jsonb, '2026-03-01T00:00:00Z'::timestamptz, ARRAY[]::text[]);
         """,
         ct: CancellationToken
      );

      var bytes = await dest.Bulk.CopyFromAsync(
         source,
         "public",
         "widgets",
         ["id", "name", "payload", "created_at", "tags"],
         CancellationToken
      );

      bytes.Should().BeGreaterThan(0);

      var rows = (await dest.Dapper.QueryAsync<WidgetRow>("SELECT id, name, payload::text AS payload, created_at, tags FROM public.widgets ORDER BY id", ct: CancellationToken)).ToArray();
      rows.Should().HaveCount(3);
      rows[0].Id.Should().Be(1);
      rows[0].Name.Should().Be("alpha");
      rows[0].Payload.Should().Contain("\"k\"");
      rows[0].Tags.Should().BeEquivalentTo(["x", "y"]);
      rows[1].Payload.Should().BeNull();
      rows[1].Tags.Should().BeNull();
      rows[2].Tags.Should().BeEmpty();
   }

   [Fact]
   public async Task CopyFromAsync_WithEmptySourceTable_CopiesZeroRows()
   {
      await using var source = new DatabaseConnection(_sourceConnectionString);
      await using var dest = new DatabaseConnection(_destConnectionString);

      const string createTable = "CREATE TABLE public.empty_table (id integer NOT NULL PRIMARY KEY);";
      await source.Dapper.ExecuteAsync(createTable, ct: CancellationToken);
      await dest.Dapper.ExecuteAsync(createTable, ct: CancellationToken);

      await dest.Bulk.CopyFromAsync(source, "public", "empty_table", ["id"], CancellationToken);

      var count = await dest.Dapper.QuerySingleAsync<long>("SELECT count(*) FROM public.empty_table", ct: CancellationToken);
      count.Should().Be(0);
   }

   [Fact]
   public async Task CopyFromAsync_WithInvalidIdentifier_Throws()
   {
      await using var source = new DatabaseConnection(_sourceConnectionString);
      await using var dest = new DatabaseConnection(_destConnectionString);

      var act = async () => await dest.Bulk.CopyFromAsync(source, "public", "drop table users;--", ["id"], CancellationToken);

      await act.Should().ThrowAsync<ArgumentException>();
   }

   [Fact]
   public async Task CopyFromAsync_WithNoColumns_Throws()
   {
      await using var source = new DatabaseConnection(_sourceConnectionString);
      await using var dest = new DatabaseConnection(_destConnectionString);

      var act = async () => await dest.Bulk.CopyFromAsync(source, "public", "any", [], CancellationToken);

      await act.Should().ThrowAsync<ArgumentException>();
   }

   private async Task<string> CreateDatabaseAsync(string dbName)
   {
      var adminConnectionString = _fixture.DbContainer.GetConnectionString();
      await using var admin = new NpgsqlConnection(adminConnectionString);
      await admin.OpenAsync(CancellationToken);

      await using (var cmd = admin.CreateCommand())
      {
         cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
         await cmd.ExecuteNonQueryAsync(CancellationToken);
      }

      var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
      {
         Database = dbName
      };
      return builder.ConnectionString;
   }

   private async Task DropDatabaseAsync(string dbName)
   {
      var adminConnectionString = _fixture.DbContainer.GetConnectionString();
      await using var admin = new NpgsqlConnection(adminConnectionString);
      await admin.OpenAsync();

      await using var cmd = admin.CreateCommand();
      cmd.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)";
      await cmd.ExecuteNonQueryAsync();
   }

   private sealed record WidgetRow
   {
      public long Id { get; init; }
      public string Name { get; init; } = "";
      public string? Payload { get; init; }
      public DateTime CreatedAt { get; init; }
      public string[]? Tags { get; init; }
   }
}
