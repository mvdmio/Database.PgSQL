using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using mvdmio.Database.PgSQL.Tool.Pull;

namespace mvdmio.Database.PgSQL.Tests.Unit.Pull;

public class SchemaExportServiceTests
{
   [Fact]
   public async Task ExportAsync_ReturnsSchemaDataFromClientAndDisposesIt()
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      var client = new FakeSchemaExportClient
      {
         Script = "-- schema",
         Tables = [new TableInfo { Schema = "public", Name = "users", Columns = [] }],
         Constraints = [new ConstraintInfo { Schema = "public", TableName = "users", ConstraintName = "pk_users", ConstraintType = "p", Definition = "PRIMARY KEY (id)" }]
      };
      var factory = new FakeSchemaExportClientFactory(client);
      var service = new SchemaExportService(factory);

      var result = await service.ExportAsync("Host=localhost;Database=mydb", cancellationToken);

      factory.ConnectionString.Should().Be("Host=localhost;Database=mydb");
      result.Script.Should().Be("-- schema");
      result.Tables.Should().ContainSingle();
      result.Constraints.Should().ContainSingle();
      client.DisposeCallCount.Should().Be(1);
   }

   private sealed class FakeSchemaExportClientFactory : ISchemaExportClientFactory
   {
      private readonly ISchemaExportClient _client;

      public FakeSchemaExportClientFactory(ISchemaExportClient client)
      {
         _client = client;
      }

      public string? ConnectionString { get; private set; }

      public ISchemaExportClient Create(string connectionString)
      {
         ConnectionString = connectionString;
         return _client;
      }
   }

   private sealed class FakeSchemaExportClient : ISchemaExportClient
   {
      public string Script { get; set; } = string.Empty;
      public IReadOnlyList<TableInfo> Tables { get; set; } = [];
      public IReadOnlyList<ConstraintInfo> Constraints { get; set; } = [];
      public int DisposeCallCount { get; private set; }

      public ValueTask DisposeAsync()
      {
         DisposeCallCount++;
         return ValueTask.CompletedTask;
      }

      public Task<string> GenerateSchemaScriptAsync(CancellationToken cancellationToken)
      {
         return Task.FromResult(Script);
      }

      public Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken cancellationToken)
      {
         return Task.FromResult(Tables);
      }

      public Task<IReadOnlyList<ConstraintInfo>> GetConstraintsAsync(CancellationToken cancellationToken)
      {
         return Task.FromResult(Constraints);
      }
   }
}
