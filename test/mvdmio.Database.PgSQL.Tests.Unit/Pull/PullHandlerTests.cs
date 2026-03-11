using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Pull;

namespace mvdmio.Database.PgSQL.Tests.Unit.Pull;

public class PullHandlerTests
{
   [Fact]
   public async Task HandleAsync_WithUnknownEnvironment_WritesErrorAndSkipsWork()
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      var schemaExportService = new FakeSchemaExportService();
      var tableDefinitionWriter = new FakeTableDefinitionWriter();
      var fileSystem = new FakePullFileSystem();
      var reporter = new FakePullReporter();
      var handler = new PullHandler(schemaExportService, tableDefinitionWriter, fileSystem, reporter);
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["prod"] = "Host=prod;Database=proddb"
         }
      };

      await handler.HandleAsync(config, null, "staging", cancellationToken);

      reporter.Errors.Should().ContainInOrder(
         "Error: Environment 'staging' not found in .mvdmio-migrations.yml.",
         "Available environments: local, prod"
      );
      schemaExportService.ConnectionString.Should().BeNull();
      tableDefinitionWriter.CallCount.Should().Be(0);
      fileSystem.CreatedDirectories.Should().BeEmpty();
      fileSystem.Writes.Should().BeEmpty();
   }

   [Fact]
   public async Task HandleAsync_WithResolvedConnection_WritesSchemaAndReportsResult()
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      var schemaExportService = new FakeSchemaExportService
      {
         Result = new SchemaExportResult(
            "-- schema",
            [new TableInfo { Schema = "public", Name = "users", Columns = [] }],
            []
         )
      };
      var tableDefinitionWriter = new FakeTableDefinitionWriter
      {
         Result = new TableDefinitionWriteResult(
            Path.Combine("C:", "repo", "Tables"),
            2,
            ["Skipped repository-ready attributes for audit.entries: no primary key was found."]
         )
      };
      var fileSystem = new FakePullFileSystem();
      var reporter = new FakePullReporter();
      var handler = new PullHandler(schemaExportService, tableDefinitionWriter, fileSystem, reporter);
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         SchemasDirectory = "Schemas",
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb"
         }
      };

      await handler.HandleAsync(config, null, null, cancellationToken);

      schemaExportService.ConnectionString.Should().Be("Host=localhost;Database=localdb");
      tableDefinitionWriter.Config.Should().BeSameAs(config);
      tableDefinitionWriter.Tables.Should().HaveCount(1);
      fileSystem.CreatedDirectories.Should().ContainSingle().Which.Should().Be(Path.GetFullPath(Path.Combine("C:", "repo", "Schemas")));
      fileSystem.Writes.Should().ContainSingle();
      fileSystem.Writes.Single().Key.Should().Be(Path.Combine(Path.GetFullPath(Path.Combine("C:", "repo", "Schemas")), "schema.local.sql"));
      fileSystem.Writes.Single().Value.Should().Be("-- schema");
      reporter.Infos.Should().ContainInOrder(
         "Connecting to database...",
         "Extracting schema...",
         string.Empty,
         $"Schema written to {Path.Combine(Path.GetFullPath(Path.Combine("C:", "repo", "Schemas")), "schema.local.sql")}",
         $"Generated 2 table definition file(s) in {Path.Combine("C:", "repo", "Tables")}"
      );
      reporter.Warnings.Should().ContainSingle().Which.Should().Be("Skipped repository-ready attributes for audit.entries: no primary key was found.");
      reporter.Errors.Should().BeEmpty();
   }

   private sealed class FakeSchemaExportService : SchemaExportService
   {
      public string? ConnectionString { get; private set; }
      public SchemaExportResult Result { get; set; } = new(string.Empty, [], []);

      public override Task<SchemaExportResult> ExportAsync(string connectionString, CancellationToken cancellationToken = default)
      {
         ConnectionString = connectionString;
         return Task.FromResult(Result);
      }
   }

   private sealed class FakeTableDefinitionWriter : TableDefinitionWriter
   {
      public ToolConfiguration? Config { get; private set; }
      public IReadOnlyList<TableInfo>? Tables { get; private set; }
      public IReadOnlyList<ConstraintInfo>? Constraints { get; private set; }
      public int CallCount { get; private set; }
      public TableDefinitionWriteResult Result { get; set; } = new(string.Empty, 0, []);

      public override Task<TableDefinitionWriteResult> WriteAsync(
         ToolConfiguration config,
         IReadOnlyList<TableInfo> tables,
         IReadOnlyList<ConstraintInfo> constraints,
         CancellationToken cancellationToken = default
      )
      {
         CallCount++;
         Config = config;
         Tables = tables;
         Constraints = constraints;
         return Task.FromResult(Result);
      }
   }

   private sealed class FakePullFileSystem : IPullFileSystem
   {
      public List<string> CreatedDirectories { get; } = [];
      public Dictionary<string, string> Writes { get; } = new(StringComparer.Ordinal);

      public void CreateDirectory(string path)
      {
         CreatedDirectories.Add(path);
      }

      public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
      {
         Writes[path] = contents;
         return Task.CompletedTask;
      }
   }

   private sealed class FakePullReporter : IPullReporter
   {
      public List<string> Infos { get; } = [];
      public List<string> Warnings { get; } = [];
      public List<string> Errors { get; } = [];

      public void WriteInfo(string message)
      {
         Infos.Add(message);
      }

      public void WriteWarning(string message)
      {
         Warnings.Add(message);
      }

      public void WriteError(string message)
      {
         Errors.Add(message);
      }
   }
}
