using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Copy;

namespace mvdmio.Database.PgSQL.Tests.Unit.Copy;

public class CopyHandlerTests
{
   [Fact]
   public async Task HandleAsync_WithUnknownSourceEnvironment_WritesError()
   {
      var ct = TestContext.Current.CancellationToken;
      var copyService = new FakeCopyService();
      var reporter = new FakeCopyReporter();
      var handler = new CopyHandler(copyService, reporter);
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         ConnectionStrings = new Dictionary<string, string>
         {
            ["test"] = "Host=localhost;Database=test"
         }
      };

      await handler.HandleAsync(config, "prod", "test", null, null, ct);

      reporter.Errors.Should().ContainInOrder(
         "Error: source environment 'prod' not found in .mvdmio-migrations.yml.",
         "Available environments: test"
      );
      copyService.WasCalled.Should().BeFalse();
   }

   [Fact]
   public async Task HandleAsync_WithUnknownDestinationEnvironment_WritesError()
   {
      var ct = TestContext.Current.CancellationToken;
      var copyService = new FakeCopyService();
      var reporter = new FakeCopyReporter();
      var handler = new CopyHandler(copyService, reporter);
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         ConnectionStrings = new Dictionary<string, string>
         {
            ["prod"] = "Host=localhost;Database=prod"
         }
      };

      await handler.HandleAsync(config, "prod", "test", null, null, ct);

      reporter.Errors.Should().ContainInOrder(
         "Error: destination environment 'test' not found in .mvdmio-migrations.yml.",
         "Available environments: prod"
      );
      copyService.WasCalled.Should().BeFalse();
   }

   [Fact]
   public async Task HandleAsync_WithIdenticalConnectionStrings_RefusesToCopy()
   {
      var ct = TestContext.Current.CancellationToken;
      var copyService = new FakeCopyService();
      var reporter = new FakeCopyReporter();
      var handler = new CopyHandler(copyService, reporter);
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         ConnectionStrings = new Dictionary<string, string>
         {
            ["a"] = "Host=localhost;Database=db",
            ["b"] = "Host=localhost;Database=db"
         }
      };

      await handler.HandleAsync(config, "a", "b", null, null, ct);

      reporter.Errors.Should().ContainSingle().Which.Should().Contain("Refusing to copy a database onto itself");
      copyService.WasCalled.Should().BeFalse();
   }

   [Fact]
   public async Task HandleAsync_WithValidEnvironments_InvokesService()
   {
      var ct = TestContext.Current.CancellationToken;
      var copyService = new FakeCopyService
      {
         Result = new CopyResult([
            new CopyTableResult("public", "users", 5, 100, false),
            new CopyTableResult("public", "orders", 10, 200, false)
         ])
      };
      var reporter = new FakeCopyReporter();
      var handler = new CopyHandler(copyService, reporter);
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         Schemas = ["public"],
         ConnectionStrings = new Dictionary<string, string>
         {
            ["prod"] = "Host=prod;Database=proddb",
            ["test"] = "Host=test;Database=testdb"
         }
      };

      await handler.HandleAsync(config, "prod", "test", null, null, ct);

      copyService.WasCalled.Should().BeTrue();
      copyService.SourceConnectionString.Should().Be("Host=prod;Database=proddb");
      copyService.DestinationConnectionString.Should().Be("Host=test;Database=testdb");
      copyService.Schemas.Should().BeEquivalentTo(["public"]);
      reporter.Errors.Should().BeEmpty();
      reporter.Infos.Should().Contain(s => s.StartsWith("Copying data from 'prod' to 'test'"));
      reporter.Infos.Should().Contain(s => s.Contains("2 table(s)") && s.Contains("15 row(s)"));
   }

   [Fact]
   public async Task HandleAsync_SchemasOverride_TakesPrecedenceOverConfig()
   {
      var ct = TestContext.Current.CancellationToken;
      var copyService = new FakeCopyService();
      var reporter = new FakeCopyReporter();
      var handler = new CopyHandler(copyService, reporter);
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         Schemas = ["public"],
         ConnectionStrings = new Dictionary<string, string>
         {
            ["prod"] = "Host=prod",
            ["test"] = "Host=test"
         }
      };

      await handler.HandleAsync(config, "prod", "test", ["billing", "identity"], null, ct);

      copyService.Schemas.Should().BeEquivalentTo(["billing", "identity"]);
   }

   [Fact]
   public async Task HandleAsync_WhenServiceThrowsInvalidOperation_WritesError()
   {
      var ct = TestContext.Current.CancellationToken;
      var copyService = new FakeCopyService { ThrowMessage = "Destination schema does not match source." };
      var reporter = new FakeCopyReporter();
      var handler = new CopyHandler(copyService, reporter);
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         ConnectionStrings = new Dictionary<string, string>
         {
            ["prod"] = "Host=prod",
            ["test"] = "Host=test"
         }
      };

      await handler.HandleAsync(config, "prod", "test", null, null, ct);

      reporter.Errors.Should().ContainSingle().Which.Should().Contain("Destination schema does not match source");
   }

   private sealed class FakeCopyService : CopyService
   {
      public bool WasCalled { get; private set; }
      public string? SourceConnectionString { get; private set; }
      public string? DestinationConnectionString { get; private set; }
      public IReadOnlyCollection<string>? Schemas { get; private set; }
      public IReadOnlyCollection<string>? ExcludeTables { get; private set; }
      public CopyResult Result { get; set; } = new([]);
      public string? ThrowMessage { get; set; }

      public override Task<CopyResult> CopyAsync(
         string sourceConnectionString,
         string destinationConnectionString,
         IReadOnlyCollection<string>? schemas,
         IReadOnlyCollection<string>? excludeTables,
         ICopyReporter reporter,
         CancellationToken cancellationToken = default
      )
      {
         WasCalled = true;
         SourceConnectionString = sourceConnectionString;
         DestinationConnectionString = destinationConnectionString;
         Schemas = schemas;
         ExcludeTables = excludeTables;

         if (ThrowMessage is not null)
            throw new InvalidOperationException(ThrowMessage);

         return Task.FromResult(Result);
      }
   }

   private sealed class FakeCopyReporter : ICopyReporter
   {
      public List<string> Infos { get; } = [];
      public List<string> Warnings { get; } = [];
      public List<string> Errors { get; } = [];

      public void WriteInfo(string message) => Infos.Add(message);
      public void WriteWarning(string message) => Warnings.Add(message);
      public void WriteError(string message) => Errors.Add(message);
   }
}
