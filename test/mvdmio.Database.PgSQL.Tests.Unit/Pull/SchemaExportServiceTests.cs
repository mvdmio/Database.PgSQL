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
         ExportableSchemas = ["billing"],
         Tables = [new TableInfo { Schema = "public", Name = "users", Columns = [] }],
         Constraints = [new ConstraintInfo { Schema = "public", TableName = "users", ConstraintName = "pk_users", ConstraintType = "p", Definition = "PRIMARY KEY (id)" }]
      };
      var factory = new FakeSchemaExportClientFactory(client);
      var service = new SchemaExportService(factory);

      var result = await service.ExportAsync("Host=localhost;Database=mydb", ["billing"], cancellationToken);

      factory.ConnectionString.Should().Be("Host=localhost;Database=mydb");
      factory.Schemas.Should().BeEquivalentTo(["billing"]);
      result.Script.Should().Be("-- schema");
      result.Tables.Should().ContainSingle();
      result.Constraints.Should().ContainSingle();
      result.Warnings.Should().BeEmpty();
      client.DisposeCallCount.Should().Be(1);
   }

   [Fact]
   public async Task ExportAsync_WithExcludedForeignKeyDependency_ReturnsWarning()
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      var client = new FakeSchemaExportClient
      {
         ExportableSchemas = ["billing"],
         Constraints =
         [
            new ConstraintInfo
            {
               Schema = "billing",
               TableName = "invoice",
               ConstraintName = "fk_invoice_user",
               ConstraintType = "f",
               ReferencedSchema = "identity",
               ReferencedTableName = "user_account",
               Definition = "FOREIGN KEY (user_id) REFERENCES identity.user_account(id)"
            }
         ]
      };
      var service = new SchemaExportService(new FakeSchemaExportClientFactory(client));

      var result = await service.ExportAsync("Host=localhost;Database=mydb", ["billing"], cancellationToken);

      result.Warnings.Should().ContainSingle();
      result.Warnings.Single().Should().Contain("billing.invoice.fk_invoice_user");
      result.Warnings.Single().Should().Contain("identity.user_account");
   }

   [Fact]
   public async Task ExportAsync_WithExcludedCustomTypeDependency_ReturnsWarning()
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      var client = new FakeSchemaExportClient
      {
         ExportableSchemas = ["billing"],
         Tables =
         [
            new TableInfo
            {
               Schema = "billing",
               Name = "invoice",
               Columns =
               [
                  new ColumnInfo
                  {
                     Name = "status",
                     DataType = "identity.user_status",
                     IsNullable = false,
                     IsIdentity = false,
                     IsGeneratedStored = false
                  }
               ]
            }
         ]
      };
      var service = new SchemaExportService(new FakeSchemaExportClientFactory(client));

      var result = await service.ExportAsync("Host=localhost;Database=mydb", ["billing"], cancellationToken);

      result.Warnings.Should().ContainSingle();
      result.Warnings.Single().Should().Contain("billing.invoice.status");
      result.Warnings.Single().Should().Contain("identity.user_status");
   }

   [Fact]
   public async Task ExportAsync_WithUnknownConfiguredSchema_ThrowsInvalidOperationException()
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      var client = new FakeSchemaExportClient
      {
         ExportableSchemas = ["billing"]
      };
      var service = new SchemaExportService(new FakeSchemaExportClientFactory(client));

      var action = () => service.ExportAsync("Host=localhost;Database=mydb", ["missing_schema"], cancellationToken);

      var exception = await action.Should().ThrowAsync<InvalidOperationException>();
      exception.Which.Message.Should().Contain("missing_schema");
   }

   private sealed class FakeSchemaExportClientFactory : ISchemaExportClientFactory
   {
      private readonly ISchemaExportClient _client;

      public FakeSchemaExportClientFactory(ISchemaExportClient client)
      {
         _client = client;
      }

      public string? ConnectionString { get; private set; }
      public IReadOnlyCollection<string>? Schemas { get; private set; }

      public ISchemaExportClient Create(string connectionString, IReadOnlyCollection<string>? schemas)
      {
         ConnectionString = connectionString;
         Schemas = schemas;
         return _client;
      }
   }

   private sealed class FakeSchemaExportClient : ISchemaExportClient
   {
      public IReadOnlyList<string> ExportableSchemas { get; set; } = [];
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

      public Task<IReadOnlyList<string>> GetExportableSchemasAsync(CancellationToken cancellationToken)
      {
         return Task.FromResult(ExportableSchemas);
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
