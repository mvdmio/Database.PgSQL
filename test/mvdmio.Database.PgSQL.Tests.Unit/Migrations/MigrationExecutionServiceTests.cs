using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using mvdmio.Database.PgSQL.Tool.Migrations;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

public class MigrationExecutionServiceTests
{
   [Fact]
   public async Task ExecuteAsync_WithNoMigrationsUpToTarget_WritesErrorAndSkipsRuntime()
   {
      var runtimeFactory = new FakeMigrationRuntimeFactory();
      var schemaResourceService = new FakeSchemaResourceService();
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(runtimeFactory, schemaResourceService, reporter);
      var project = CreateProjectContext([new FakeDbMigration(202602161500)]);

      await service.ExecuteAsync(
         MigrateRequest.To(202602161400),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      reporter.Errors.Should().ContainSingle().Which.Should().Be("Error: No migrations found with identifier <= 202602161400.");
      runtimeFactory.CallCount.Should().Be(0);
   }

   [Fact]
   public async Task ExecuteAsync_LatestWithPendingMigrations_RunsLatestMigrationPath()
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = false,
         AlreadyExecuted = [new ExecutedMigrationModel(202602161430, "Initial", DateTime.UtcNow)],
         FinalExecuted =
         [
            new ExecutedMigrationModel(202602161430, "Initial", DateTime.UtcNow),
            new ExecutedMigrationModel(202602161530, "AddUsers", DateTime.UtcNow)
         ]
      };
      var runtimeFactory = new FakeMigrationRuntimeFactory { Runtime = runtime };
      var schemaResourceService = new FakeSchemaResourceService();
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(runtimeFactory, schemaResourceService, reporter);
      var project = CreateProjectContext([new FakeDbMigration(202602161430), new FakeDbMigration(202602161530)]);

      await service.ExecuteAsync(
         MigrateRequest.Latest,
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      runtimeFactory.CallCount.Should().Be(1);
      runtime.MigrateLatestCallCount.Should().Be(1);
      runtime.MigrateToCallCount.Should().Be(0);
      reporter.Infos.Should().Contain("Found 2 migration(s), 1 already applied.");
      reporter.Infos.Should().Contain("Migration complete. 1 migration(s) applied.");
   }

   [Fact]
   public async Task ExecuteAsync_TargetWithNewerSchema_ReportsAndRunsTargetMigrations()
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = true,
         AlreadyExecuted = [],
         FinalExecuted = [new ExecutedMigrationModel(202602161430, "Initial", DateTime.UtcNow)]
      };
      var runtimeFactory = new FakeMigrationRuntimeFactory { Runtime = runtime };
      var schemaResourceService = new FakeSchemaResourceService
      {
         SchemaExists = true,
         SchemaResourceName = "schema.local.sql",
         SchemaContent = "-- Migration version: 202602161500 (FutureSchema)"
      };
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(runtimeFactory, schemaResourceService, reporter);
      var project = CreateProjectContext([new FakeDbMigration(202602161430), new FakeDbMigration(202602161530)]);

      await service.ExecuteAsync(
         MigrateRequest.To(202602161430),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      reporter.Infos.Should().Contain("Schema version (202602161500) is newer than target (202602161430). Running migrations instead.");
      runtime.MigrateToCallCount.Should().Be(1);
      runtime.MigrateToTarget.Should().Be(202602161430);
   }

   private static MigrationProjectContext CreateProjectContext(IReadOnlyList<IDbMigration> migrations)
   {
      return new MigrationProjectContext(
         typeof(MigrationExecutionServiceTests).Assembly,
         new FakeMigrationRetriever(migrations),
         migrations
      );
   }

   private sealed class FakeMigrationRuntimeFactory : IMigrationRuntimeFactory
   {
      public int CallCount { get; private set; }
      public FakeMigrationRuntime Runtime { get; set; } = new();

      public IMigrationRuntime Create(string connectionString, string? environmentName, MigrationProjectContext project)
      {
         CallCount++;
         Runtime.ConnectionString = connectionString;
         Runtime.EnvironmentName = environmentName;
         Runtime.Project = project;
         return Runtime;
      }
   }

   private sealed class FakeMigrationRuntime : IMigrationRuntime
   {
      public string? ConnectionString { get; set; }
      public string? EnvironmentName { get; set; }
      public MigrationProjectContext? Project { get; set; }
      public bool IsDatabaseEmptyResult { get; set; }
      public IReadOnlyList<ExecutedMigrationModel> AlreadyExecuted { get; set; } = [];
      public IReadOnlyList<ExecutedMigrationModel> FinalExecuted { get; set; } = [];
      public int MigrateLatestCallCount { get; private set; }
      public int MigrateToCallCount { get; private set; }
      public long? MigrateToTarget { get; private set; }

      public ValueTask DisposeAsync()
      {
         return ValueTask.CompletedTask;
      }

      public Task<bool> IsDatabaseEmptyAsync(CancellationToken cancellationToken)
      {
         return Task.FromResult(IsDatabaseEmptyResult);
      }

      public Task<IEnumerable<ExecutedMigrationModel>> RetrieveAlreadyExecutedMigrationsAsync(CancellationToken cancellationToken)
      {
         var result = MigrateLatestCallCount > 0 || MigrateToCallCount > 0 ? FinalExecuted : AlreadyExecuted;
         return Task.FromResult(result.AsEnumerable());
      }

      public Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken)
      {
         MigrateLatestCallCount++;
         return Task.CompletedTask;
      }

      public Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken)
      {
         MigrateToCallCount++;
         MigrateToTarget = targetIdentifier;
         return Task.CompletedTask;
      }
   }

   private sealed class FakeSchemaResourceService : ISchemaResourceService
   {
      public bool SchemaExists { get; set; }
      public string? SchemaResourceName { get; set; }
      public string? SchemaContent { get; set; }

      public bool SchemaResourceExists(MigrationProjectContext project, string? environmentName)
      {
         return SchemaExists;
      }

      public string? GetSchemaResourceName(MigrationProjectContext project, string? environmentName)
      {
         return SchemaResourceName;
      }

      public Task<string?> ReadSchemaContentAsync(MigrationProjectContext project, string? environmentName, CancellationToken cancellationToken)
      {
         return Task.FromResult(SchemaContent);
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
      private readonly IReadOnlyList<IDbMigration> _migrations;

      public FakeMigrationRetriever(IReadOnlyList<IDbMigration> migrations)
      {
         _migrations = migrations;
      }

      public IEnumerable<IDbMigration> RetrieveMigrations()
      {
         return _migrations;
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
