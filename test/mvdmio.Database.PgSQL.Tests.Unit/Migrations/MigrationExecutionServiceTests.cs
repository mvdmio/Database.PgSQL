using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
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

      var exitCode = await service.ExecuteAsync(
         MigrateRequest.To(202602161400, DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      exitCode.Should().Be(1);
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

      var exitCode = await service.ExecuteAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      exitCode.Should().Be(0);
      runtimeFactory.CallCount.Should().Be(1);
      runtime.MigrateLatestCallCount.Should().Be(1);
      runtime.MigrateToCallCount.Should().Be(0);
      reporter.Infos.Should().Contain("Found 2 migration(s), 1 already applied.");
      reporter.Infos.Should().Contain("Migration complete. 1 migration(s) applied.");
   }

   [Fact]
   public async Task ExecuteAsync_LatestWithSameIdentifierExecutedInAnotherScope_StillRunsMigrator()
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = false,
         AlreadyExecuted = [new ExecutedMigrationModel(202602161430, "OtherScopeMigration", DateTime.UtcNow, "Other.Scope")],
         FinalExecuted =
         [
            new ExecutedMigrationModel(202602161430, "OtherScopeMigration", DateTime.UtcNow, "Other.Scope"),
            new ExecutedMigrationModel(202602161430, "Migration202602161430", DateTime.UtcNow, ThisAssemblyScope)
         ]
      };
      var runtimeFactory = new FakeMigrationRuntimeFactory { Runtime = runtime };
      var service = new MigrationExecutionService(runtimeFactory, new FakeSchemaResourceService(), new FakeMigrateReporter());
      var project = CreateProjectContext([new FakeDbMigration(202602161430)]);

      await service.ExecuteAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      runtime.MigrateLatestCallCount.Should().Be(1);
   }

   [Fact]
   public async Task ExecuteAsync_LatestWithScopeBaselineCoveringAllMigrations_ReportsUpToDateAndSkipsMigrator()
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = false,
         AlreadyExecuted = [new ExecutedMigrationModel(202602161600, "Baseline", DateTime.UtcNow, ThisAssemblyScope)]
      };
      var runtimeFactory = new FakeMigrationRuntimeFactory { Runtime = runtime };
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(runtimeFactory, new FakeSchemaResourceService(), reporter);
      var project = CreateProjectContext([new FakeDbMigration(202602161430), new FakeDbMigration(202602161530)]);

      var exitCode = await service.ExecuteAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      exitCode.Should().Be(0);
      reporter.Infos.Should().Contain("Database is already up to date.");
      runtime.MigrateLatestCallCount.Should().Be(0);
   }

   [Fact]
   public async Task ExecuteAsync_LatestWithOnlyOutOfOrderMigrationPending_RunsMigrator()
   {
      var (runtime, reporter, _) = await ExecuteWithOutOfOrderMigrationAsync(MigrateRequest.Latest(DeployPhase.PreDeploy));

      reporter.Infos.Should().NotContain("Database is already up to date.");
      runtime.MigrateLatestCallCount.Should().Be(1);
      reporter.Infos.Should().Contain("Migration complete. 1 migration(s) applied.");
   }

   [Theory]
   [InlineData(202602161500)]
   [InlineData(202602161600)]
   public async Task ExecuteAsync_TargetAtOrAboveOutOfOrderMigration_RunsMigratorToTarget(long target)
   {
      var (runtime, reporter, _) = await ExecuteWithOutOfOrderMigrationAsync(MigrateRequest.To(target, DeployPhase.PreDeploy));

      reporter.Infos.Should().NotContain("Database is already up to date for the specified target.");
      runtime.MigrateToCallCount.Should().Be(1);
      runtime.MigrateToTarget.Should().Be(target);
   }

   [Fact]
   public async Task ExecuteAsync_TargetBelowOutOfOrderMigration_ReportsUpToDateAndSkipsMigrator()
   {
      var (runtime, reporter, exitCode) = await ExecuteWithOutOfOrderMigrationAsync(MigrateRequest.To(202602161430, DeployPhase.PreDeploy));

      exitCode.Should().Be(0);
      reporter.Infos.Should().Contain("Database is already up to date for the specified target.");
      runtime.MigrateToCallCount.Should().Be(0);
   }

   [Fact]
   public async Task ExecuteAsync_LatestOnSharedDatabaseWithEmptyVouchedScope_ReportsSchemaPathAndRunsMigrator()
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = false,
         AlreadyExecuted = [new ExecutedMigrationModel(202602161430, "OtherBaseline", DateTime.UtcNow, "Other.Scope")],
         FinalExecuted =
         [
            new ExecutedMigrationModel(202602161430, "OtherBaseline", DateTime.UtcNow, "Other.Scope"),
            new ExecutedMigrationModel(202602161530, "Baseline", DateTime.UtcNow, ThisAssemblyScope)
         ]
      };
      var runtimeFactory = new FakeMigrationRuntimeFactory { Runtime = runtime };
      var schemaResourceService = new FakeSchemaResourceService
      {
         SchemaExists = true,
         SchemaResourceName = "schema.local.sql",
         SchemaContent = "-- Migration version: 202602161530 (Baseline) [mvdmio.Database.PgSQL.Tests.Unit]"
      };
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(runtimeFactory, schemaResourceService, reporter);
      var project = CreateProjectContext([new FakeDbMigration(202602161430), new FakeDbMigration(202602161530)]);

      await service.ExecuteAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      reporter.Infos.Should().Contain("Empty vouched scope detected. Will apply embedded schema: schema.local.sql");
      reporter.Infos.Should().NotContain("Database is already up to date.");
      reporter.Infos.Should().NotContain(info => info.Contains("Empty database detected", StringComparison.Ordinal));
      runtime.MigrateLatestCallCount.Should().Be(1);
   }

   [Fact]
   public async Task ExecuteAsync_LatestOnSharedDatabaseWithFoldedSchemaAndEmptyScope_RunsMigratorDespiteZeroPending()
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = false,
         AlreadyExecuted = [new ExecutedMigrationModel(202601010000, "OtherBaseline", DateTime.UtcNow, "Other.Scope")],
         FinalExecuted =
         [
            new ExecutedMigrationModel(202601010000, "OtherBaseline", DateTime.UtcNow, "Other.Scope"),
            new ExecutedMigrationModel(202602161530, "Baseline", DateTime.UtcNow, ThisAssemblyScope)
         ]
      };
      var runtimeFactory = new FakeMigrationRuntimeFactory { Runtime = runtime };
      var schemaResourceService = new FakeSchemaResourceService
      {
         SchemaExists = true,
         SchemaResourceName = "schema.local.sql",
         SchemaContent = "-- Migration version: 202602161530 (Baseline)"
      };
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(runtimeFactory, schemaResourceService, reporter);
      var project = CreateProjectContext([]);

      await service.ExecuteAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      reporter.Infos.Should().Contain("Empty vouched scope detected. Will apply embedded schema: schema.local.sql");
      reporter.Infos.Should().NotContain(info => info.Contains("Empty database detected", StringComparison.Ordinal));
      runtime.MigrateLatestCallCount.Should().Be(1);
   }

   [Fact]
   public async Task ExecuteAsync_LatestOnEmptyDatabaseWithSchema_ReportsSchemaPathWithoutCallingItEmpty()
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = true,
         AlreadyExecuted = [],
         FinalExecuted = [new ExecutedMigrationModel(202602161530, "Baseline", DateTime.UtcNow, ThisAssemblyScope)]
      };
      var runtimeFactory = new FakeMigrationRuntimeFactory { Runtime = runtime };
      var schemaResourceService = new FakeSchemaResourceService
      {
         SchemaExists = true,
         SchemaResourceName = "schema.local.sql",
         SchemaContent = "-- Migration version: 202602161530 (Baseline)"
      };
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(runtimeFactory, schemaResourceService, reporter);
      var project = CreateProjectContext([new FakeDbMigration(202602161430)]);

      await service.ExecuteAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      reporter.Infos.Should().Contain("Empty vouched scope detected. Will apply embedded schema: schema.local.sql");
      reporter.Infos.Should().NotContain(info => info.Contains("Empty database detected", StringComparison.Ordinal));
      runtime.MigrateLatestCallCount.Should().Be(1);
   }

   [Fact]
   public async Task ExecuteAsync_LatestWithSchemaAndScopeWatermark_ReportsUpToDateWithoutClaimingSchema()
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = false,
         AlreadyExecuted = [new ExecutedMigrationModel(202602161600, "Baseline", DateTime.UtcNow, ThisAssemblyScope)]
      };
      var runtimeFactory = new FakeMigrationRuntimeFactory { Runtime = runtime };
      var schemaResourceService = new FakeSchemaResourceService
      {
         SchemaExists = true,
         SchemaResourceName = "schema.local.sql",
         SchemaContent = "-- Migration version: 202602161600 (Baseline)"
      };
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(runtimeFactory, schemaResourceService, reporter);
      var project = CreateProjectContext([new FakeDbMigration(202602161430), new FakeDbMigration(202602161530)]);

      await service.ExecuteAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      reporter.Infos.Should().Contain("Database is already up to date.");
      reporter.Infos.Should().NotContain(info => info.Contains("Will apply embedded schema", StringComparison.Ordinal));
      runtime.MigrateLatestCallCount.Should().Be(0);
   }

   private static string ThisAssemblyScope => typeof(MigrationExecutionServiceTests).Assembly.GetName().Name!;

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
         MigrateRequest.To(202602161430, DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      reporter.Infos.Should().Contain("Schema version (202602161500) is newer than target (202602161430). Running migrations instead.");
      reporter.Infos.Should().NotContain(info => info.Contains("Empty database detected", StringComparison.Ordinal));
      runtime.MigrateToCallCount.Should().Be(1);
      runtime.MigrateToTarget.Should().Be(202602161430);
   }

   [Fact]
   public async Task ExecuteAsync_TargetOnSharedDatabaseWithSchemaAtOrBelowTarget_ReportsSchemaPath()
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = false,
         AlreadyExecuted = [new ExecutedMigrationModel(202602161430, "OtherBaseline", DateTime.UtcNow, "Other.Scope")],
         FinalExecuted =
         [
            new ExecutedMigrationModel(202602161430, "OtherBaseline", DateTime.UtcNow, "Other.Scope"),
            new ExecutedMigrationModel(202602161430, "Baseline", DateTime.UtcNow, ThisAssemblyScope)
         ]
      };
      var runtimeFactory = new FakeMigrationRuntimeFactory { Runtime = runtime };
      var schemaResourceService = new FakeSchemaResourceService
      {
         SchemaExists = true,
         SchemaResourceName = "schema.local.sql",
         SchemaContent = "-- Migration version: 202602161430 (Baseline)"
      };
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(runtimeFactory, schemaResourceService, reporter);
      var project = CreateProjectContext([new FakeDbMigration(202602161430)]);

      await service.ExecuteAsync(
         MigrateRequest.To(202602161430, DeployPhase.PreDeploy),
         "Host=localhost;Database=mydb",
         "local",
         project,
         TestContext.Current.CancellationToken
      );

      reporter.Infos.Should().Contain("Empty vouched scope detected. Will apply embedded schema: schema.local.sql");
      reporter.Infos.Should().NotContain(info => info.Contains("Empty database detected", StringComparison.Ordinal));
      runtime.MigrateToCallCount.Should().Be(1);
      runtime.MigrateToTarget.Should().Be(202602161430);
   }

   [Fact]
   public async Task ExecuteAsync_PreDeployRunWithNewTwoPartMigration_ReportsPendingPreDeployCountAndAllWaitingPostDeployParts()
   {
      var (runtime, reporter, exitCode) = await ExecuteTwoPartScenarioAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         alreadyExecuted: [Row(100), Row(200)],
         finalExecuted: [Row(100), Row(200), Row(300)],
         migrations: [new FakeDbMigration(100), new FakeTwoPartMigration(200), new FakeTwoPartMigration(300)]
      );

      exitCode.Should().Be(0);
      runtime.MigratePhase.Should().Be(DeployPhase.PreDeploy);
      reporter.Infos.Should().Contain("Found 3 migration(s), 2 already applied.");
      reporter.Infos.Should().Contain("Pending pre-deploy part(s): 1.");
      reporter.Infos.Should().NotContain(info => info.StartsWith("Pending post-deploy", StringComparison.Ordinal));
      reporter.Infos.Should().Contain("Migration complete. 1 migration(s) applied.");
      reporter.Infos.Should().Contain("2 post-deploy part(s) waiting for a post-deploy run.");
   }

   [Fact]
   public async Task ExecuteAsync_PreDeployRunWhenUpToDateWithPostDeployPartWaiting_ReportsUpToDateAndWaitingCount()
   {
      var (runtime, reporter, exitCode) = await ExecuteTwoPartScenarioAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         alreadyExecuted: [Row(100), Row(200)],
         finalExecuted: [],
         migrations: [new FakeDbMigration(100), new FakeTwoPartMigration(200)]
      );

      exitCode.Should().Be(0);
      runtime.MigrateLatestCallCount.Should().Be(0);
      reporter.Infos.Should().Contain("Pending pre-deploy part(s): 0.");
      reporter.Infos.Should().Contain("Database is already up to date.");
      reporter.Infos.Should().Contain("1 post-deploy part(s) waiting for a post-deploy run.");
      reporter.Infos.Should().ContainSingle(info => info.StartsWith("Warning: The post-deploy part of migration 200 ", StringComparison.Ordinal) && info.Contains("is waiting from an earlier run", StringComparison.Ordinal));
   }

   [Fact]
   public async Task ExecuteAsync_PostDeployRunOnFreshDatabase_WarnsThatThePreDeployPartIsPending()
   {
      var (runtime, reporter, exitCode) = await ExecuteTwoPartScenarioAsync(
         MigrateRequest.Latest(DeployPhase.PostDeploy),
         alreadyExecuted: [],
         finalExecuted: [],
         migrations: [new FakeDbMigration(100), new FakeTwoPartMigration(200)]
      );

      exitCode.Should().Be(0);
      runtime.MigrateLatestCallCount.Should().Be(0);
      reporter.Infos.Should().Contain("Database is already up to date.");
      reporter.Infos.Should().ContainSingle(info => info.StartsWith("Warning: The post-deploy part of migration 200 ", StringComparison.Ordinal) && info.Contains("its pre-deploy part is still pending", StringComparison.Ordinal));
   }

   [Fact]
   public async Task ExecuteAsync_PreDeployRunWithNoPostDeployPartWaiting_PrintsNoWaitingLine()
   {
      var (_, reporter, _) = await ExecuteTwoPartScenarioAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         alreadyExecuted: [Row(100)],
         finalExecuted: [Row(100), Row(200)],
         migrations: [new FakeDbMigration(100), new FakeDbMigration(200)]
      );

      reporter.Infos.Should().NotContain(info => info.Contains("waiting for a post-deploy run", StringComparison.Ordinal));
   }

   [Fact]
   public async Task ExecuteAsync_PostDeployRun_ReportsOnlyPostDeployCountAndSkipsPartWhosePreDeployPartIsPending()
   {
      var (runtime, reporter, exitCode) = await ExecuteTwoPartScenarioAsync(
         MigrateRequest.Latest(DeployPhase.PostDeploy),
         alreadyExecuted: [Row(100), Row(200)],
         finalExecuted: [Row(100), Row(200), Row(200, DeployPhase.PostDeploy)],
         migrations: [new FakeDbMigration(100), new FakeTwoPartMigration(200), new FakeTwoPartMigration(300)]
      );

      exitCode.Should().Be(0);
      runtime.MigratePhase.Should().Be(DeployPhase.PostDeploy);
      reporter.Infos.Should().Contain("Pending post-deploy part(s): 1.");
      reporter.Infos.Should().NotContain(info => info.StartsWith("Pending pre-deploy", StringComparison.Ordinal));
      reporter.Infos.Should().Contain("Migration complete. 1 migration(s) applied.");
      reporter.Infos.Should().NotContain(info => info.Contains("waiting for a post-deploy run", StringComparison.Ordinal));
   }

   [Fact]
   public async Task ExecuteAsync_PostDeployRunWithOnlyPartWhosePreDeployPartIsPending_CountsNothingAndReportsUpToDate()
   {
      var (runtime, reporter, exitCode) = await ExecuteTwoPartScenarioAsync(
         MigrateRequest.Latest(DeployPhase.PostDeploy),
         alreadyExecuted: [Row(100)],
         finalExecuted: [],
         migrations: [new FakeDbMigration(100), new FakeTwoPartMigration(200)]
      );

      exitCode.Should().Be(0);
      runtime.MigrateLatestCallCount.Should().Be(0);
      reporter.Infos.Should().Contain("Pending post-deploy part(s): 0.");
      reporter.Infos.Should().Contain("Database is already up to date.");
   }

   [Fact]
   public async Task ExecuteAsync_AllRun_ReportsBothPendingCountsAndNoWaitingLine()
   {
      var (runtime, reporter, _) = await ExecuteTwoPartScenarioAsync(
         MigrateRequest.Latest(DeployPhase.All),
         alreadyExecuted: [Row(100)],
         finalExecuted: [Row(100), Row(200), Row(200, DeployPhase.PostDeploy)],
         migrations: [new FakeDbMigration(100), new FakeTwoPartMigration(200)]
      );

      runtime.MigratePhase.Should().Be(DeployPhase.All);
      reporter.Infos.Should().Contain("Pending pre-deploy part(s): 1.");
      reporter.Infos.Should().Contain("Pending post-deploy part(s): 1.");
      reporter.Infos.Should().Contain("Migration complete. 1 migration(s) applied.");
      reporter.Infos.Should().NotContain(info => info.Contains("waiting for a post-deploy run", StringComparison.Ordinal));
   }

   [Fact]
   public async Task ExecuteAsync_WithPostDeployRowsRecorded_CountsOnlyPreDeployRowsAsAlreadyApplied()
   {
      var (_, reporter, _) = await ExecuteTwoPartScenarioAsync(
         MigrateRequest.Latest(DeployPhase.PreDeploy),
         alreadyExecuted: [Row(100), Row(200), Row(200, DeployPhase.PostDeploy)],
         finalExecuted: [],
         migrations: [new FakeDbMigration(100), new FakeTwoPartMigration(200)]
      );

      reporter.Infos.Should().Contain("Found 2 migration(s), 2 already applied.");
   }

   [Theory]
   [InlineData(DeployPhase.PreDeploy)]
   [InlineData(DeployPhase.PostDeploy)]
   [InlineData(DeployPhase.All)]
   public async Task ExecuteAsync_TargetRunWithPhase_PassesPhaseToRuntime(DeployPhase phase)
   {
      var (runtime, _, _) = await ExecuteTwoPartScenarioAsync(
         MigrateRequest.To(300, phase),
         alreadyExecuted: [Row(100), Row(200)],
         finalExecuted: [Row(100), Row(200), Row(300)],
         migrations: [new FakeTwoPartMigration(100), new FakeTwoPartMigration(200), new FakeDbMigration(300)]
      );

      runtime.MigrateToCallCount.Should().Be(1);
      runtime.MigratePhase.Should().Be(phase);
      runtime.MigrateToTarget.Should().Be(300);
   }

   private static ExecutedMigrationModel Row(long identifier, DeployPhase phase = DeployPhase.PreDeploy)
   {
      return new ExecutedMigrationModel(identifier, $"Migration{identifier}", DateTime.UtcNow, ThisAssemblyScope) { Phase = phase };
   }

   private static async Task<(FakeMigrationRuntime Runtime, FakeMigrateReporter Reporter, int ExitCode)> ExecuteTwoPartScenarioAsync(
      MigrateRequest request,
      ExecutedMigrationModel[] alreadyExecuted,
      ExecutedMigrationModel[] finalExecuted,
      IReadOnlyList<IDbMigration> migrations
   )
   {
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = false,
         AlreadyExecuted = alreadyExecuted,
         FinalExecuted = finalExecuted
      };
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(new FakeMigrationRuntimeFactory { Runtime = runtime }, new FakeSchemaResourceService(), reporter);

      var exitCode = await service.ExecuteAsync(request, "Host=localhost;Database=mydb", "local", CreateProjectContext(migrations), TestContext.Current.CancellationToken);

      return (runtime, reporter, exitCode);
   }

   private static MigrationProjectContext CreateProjectContext(IReadOnlyList<IDbMigration> migrations)
   {
      return new MigrationProjectContext(
         typeof(MigrationExecutionServiceTests).Assembly,
         new FakeMigrationRetriever(migrations),
         migrations
      );
   }

   private static async Task<(FakeMigrationRuntime Runtime, FakeMigrateReporter Reporter, int ExitCode)> ExecuteWithOutOfOrderMigrationAsync(MigrateRequest request)
   {
      ExecutedMigrationModel[] rows =
      [
         new(202602161400, "Migration202602161400", DateTime.UtcNow, ThisAssemblyScope),
         new(202602161600, "Migration202602161600", DateTime.UtcNow, ThisAssemblyScope)
      ];
      var runtime = new FakeMigrationRuntime
      {
         IsDatabaseEmptyResult = false,
         AlreadyExecuted = rows,
         FinalExecuted = [.. rows, new(202602161500, "Migration202602161500", DateTime.UtcNow, ThisAssemblyScope)]
      };
      var reporter = new FakeMigrateReporter();
      var service = new MigrationExecutionService(new FakeMigrationRuntimeFactory { Runtime = runtime }, new FakeSchemaResourceService(), reporter);
      var project = CreateProjectContext([new FakeDbMigration(202602161400), new FakeDbMigration(202602161500), new FakeDbMigration(202602161600)]);

      var exitCode = await service.ExecuteAsync(request, "Host=localhost;Database=mydb", "local", project, TestContext.Current.CancellationToken);

      return (runtime, reporter, exitCode);
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
      public DeployPhase? MigratePhase { get; private set; }

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

      public Task MigrateDatabaseToLatestAsync(DeployPhase phase, CancellationToken cancellationToken)
      {
         MigrateLatestCallCount++;
         MigratePhase = phase;
         return Task.CompletedTask;
      }

      public Task MigrateDatabaseToAsync(long targetIdentifier, DeployPhase phase, CancellationToken cancellationToken)
      {
         MigrateToCallCount++;
         MigratePhase = phase;
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

   private sealed class FakeTwoPartMigration : IDbMigration
   {
      public FakeTwoPartMigration(long identifier)
      {
         Identifier = identifier;
      }

      public long Identifier { get; }
      public string Name => $"TwoPart{Identifier}";

      public Task PreDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }

      public Task PostDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
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
