using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Migrations;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

public class MigrateHandlerTests
{
   [Fact]
   public async Task HandleAsync_WithUnknownEnvironment_WritesErrorAndSkipsExecution()
   {
      var projectLoader = new FakeMigrationProjectLoader();
      var executionService = new FakeMigrationExecutionService();
      var reporter = new FakeMigrateReporter();
      var handler = new MigrateHandler(projectLoader, executionService, reporter);
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb"
         }
      };

      await handler.HandleAsync(MigrateRequest.Latest, config, null, "prod", TestContext.Current.CancellationToken);

      reporter.Errors.Should().ContainInOrder(
         "Error: Environment 'prod' not found in .mvdmio-migrations.yml.",
         "Available environments: local"
      );
      projectLoader.LoadedProjectPath.Should().BeNull();
      executionService.CallCount.Should().Be(0);
   }

   [Fact]
   public async Task HandleAsync_WithResolvedConnection_LoadsProjectAndExecutesMigration()
   {
      var project = new MigrationProjectContext(
         typeof(MigrateHandlerTests).Assembly,
         new FakeMigrationRetriever(),
         [new FakeDbMigration(202602161430), new FakeDbMigration(202602161530)]
      );
      var projectLoader = new FakeMigrationProjectLoader { Result = project };
      var executionService = new FakeMigrationExecutionService();
      var reporter = new FakeMigrateReporter();
      var handler = new MigrateHandler(projectLoader, executionService, reporter);
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         Project = Path.Combine("src", "MyApp.Data"),
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb"
         }
      };
      var request = MigrateRequest.To(202602161430);

      await handler.HandleAsync(request, config, null, null, TestContext.Current.CancellationToken);

      projectLoader.LoadedProjectPath.Should().Be(Path.GetFullPath(Path.Combine("C:", "repo", "src", "MyApp.Data")));
      executionService.CallCount.Should().Be(1);
      executionService.Request.Should().Be(request);
      executionService.ConnectionString.Should().Be("Host=localhost;Database=localdb");
      executionService.EnvironmentName.Should().Be("local");
      executionService.Project.Should().BeSameAs(project);
      reporter.Errors.Should().BeEmpty();
   }

   private sealed class FakeMigrationProjectLoader : MigrationProjectLoader
   {
      public string? LoadedProjectPath { get; private set; }
      public MigrationProjectContext Result { get; set; } = new(
         typeof(MigrateHandlerTests).Assembly,
         new FakeMigrationRetriever(),
         []
      );

      public override MigrationProjectContext Load(string projectPath)
      {
         LoadedProjectPath = projectPath;
         return Result;
      }
   }

   private sealed class FakeMigrationExecutionService : MigrationExecutionService
   {
      public int CallCount { get; private set; }
      public MigrateRequest? Request { get; private set; }
      public string? ConnectionString { get; private set; }
      public string? EnvironmentName { get; private set; }
      public MigrationProjectContext? Project { get; private set; }

      public override Task ExecuteAsync(
         MigrateRequest request,
         string connectionString,
         string? environmentName,
         MigrationProjectContext project,
         CancellationToken cancellationToken = default
      )
      {
         CallCount++;
         Request = request;
         ConnectionString = connectionString;
         EnvironmentName = environmentName;
         Project = project;
         return Task.CompletedTask;
      }
   }

   private sealed class FakeMigrateReporter : IMigrateReporter
   {
      public List<string> Infos { get; } = [];
      public List<string> Errors { get; } = [];

      public void WriteInfo(string message)
      {
         Infos.Add(message);
      }

      public void WriteError(string message)
      {
         Errors.Add(message);
      }
   }

   private sealed class FakeMigrationRetriever : mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces.IMigrationRetriever
   {
      public IEnumerable<IDbMigration> RetrieveMigrations()
      {
         return [];
      }
   }

   private sealed class FakeDbMigration : IDbMigration
   {
      public FakeDbMigration(long identifier)
      {
         Identifier = identifier;
      }

      public long Identifier { get; }
      public string Name => $"Migration{Identifier}";

      public Task UpAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }
}
