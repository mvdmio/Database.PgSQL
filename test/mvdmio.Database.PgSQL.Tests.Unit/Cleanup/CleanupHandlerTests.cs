using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using mvdmio.Database.PgSQL.Tool.Cleanup;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Migrations;
using mvdmio.Database.PgSQL.Tool.Pull;

namespace mvdmio.Database.PgSQL.Tests.Unit.Cleanup;

public class CleanupHandlerTests : IDisposable
{
   private const string SCOPE = "ScopeA";
   private const string DEV = "Host=dev;Database=app";
   private const string PROD = "Host=prod;Database=app";

   private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"cleanup-handler-tests-{Guid.NewGuid():N}");
   private readonly FakeSchemaExportService _schemaExportService = new();
   private readonly FakeMigrationRuntimeFactory _runtimeFactory = new();
   private readonly FakeCleanupReporter _reporter = new();

   public CleanupHandlerTests()
   {
      Directory.CreateDirectory(MigrationsDirectory);
   }

   private string MigrationsDirectory => Path.Combine(_basePath, "Migrations");

   [Fact]
   public async Task HandleAsync_WhenProjectBuildFails_TouchesNoEnvironmentAndDeletesNothing()
   {
      var obsolete = CreateMigrationFile("_202602151200_CreateUsers.cs");
      var handler = CreateHandler(new FakeMigrationProjectLoader { Failure = new InvalidOperationException("Build failed. See output above for details.") });

      var act = () => handler.HandleAsync(Config(), TestContext.Current.CancellationToken);

      await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Build failed. See output above for details.");
      _schemaExportService.ConnectionStrings.Should().BeEmpty();
      _runtimeFactory.ConnectionStrings.Should().BeEmpty();
      File.Exists(Path.Combine(_basePath, "Schemas", "schema.dev.sql")).Should().BeFalse();
      File.Exists(obsolete).Should().BeTrue();
   }

   [Fact]
   public async Task HandleAsync_WithFilePendingInOneEnvironment_KeepsAndReportsItAndDeletesFileEveryEnvironmentHas()
   {
      var everywhere = CreateMigrationFile("_202602151200_CreateUsers.cs");
      var outOfOrderInProd = CreateMigrationFile("_202602161000_CreateOrders.cs");
      _runtimeFactory.RowsByConnectionString[DEV] = Executed(202602151200, 202602161000, 202602170000);
      _runtimeFactory.RowsByConnectionString[PROD] = Executed(202602151200, 202602170000);

      var projectLoader = Project(202602151200, 202602161000, 202602170000);

      var exitCode = await CreateHandler(projectLoader).HandleAsync(Config(), TestContext.Current.CancellationToken);

      exitCode.Should().Be(0);
      projectLoader.LoadedWith.Should().Be(TestContext.Current.CancellationToken);
      File.Exists(everywhere).Should().BeFalse();
      File.Exists(outOfOrderInProd).Should().BeTrue();
      _schemaExportService.ConnectionStrings.Should().Equal(DEV, PROD);
      _reporter.Infos.Should().ContainInOrder(
         "Lowest migration version across environments: 202602170000",
         $"Kept {outOfOrderInProd}: still pending in prod",
         $"Deleted {everywhere}",
         string.Empty,
         "Cleanup complete. Deleted 1 migration file(s)."
      );
      _reporter.Errors.Should().BeEmpty();
   }

   [Fact]
   public async Task HandleAsync_WhenEveryFileBelowTheBoundIsPending_ReportsThatNothingWasDeleted()
   {
      var outOfOrderInProd = CreateMigrationFile("_202602161000_CreateOrders.cs");
      _runtimeFactory.RowsByConnectionString[DEV] = Executed(202602151200, 202602161000, 202602170000);
      _runtimeFactory.RowsByConnectionString[PROD] = Executed(202602151200, 202602170000);

      await CreateHandler(Project(202602151200, 202602161000, 202602170000)).HandleAsync(Config(), TestContext.Current.CancellationToken);

      File.Exists(outOfOrderInProd).Should().BeTrue();
      _reporter.Infos.Should().ContainInOrder($"Kept {outOfOrderInProd}: still pending in prod", "No migration files were deleted.");
      _reporter.Infos.Should().NotContain("No migration files are older than the lowest environment version.");
   }

   [Fact]
   public async Task HandleAsync_WithNoFileBelowTheBound_ReportsThatNoFileIsOlderThanTheLowestVersion()
   {
      var current = CreateMigrationFile("_202602170000_CreateInvoices.cs");
      _runtimeFactory.RowsByConnectionString[DEV] = Executed(202602151200, 202602170000);
      _runtimeFactory.RowsByConnectionString[PROD] = Executed(202602151200, 202602170000);

      var exitCode = await CreateHandler(Project(202602151200, 202602170000)).HandleAsync(Config(), TestContext.Current.CancellationToken);

      exitCode.Should().Be(0);
      File.Exists(current).Should().BeTrue();
      _reporter.Infos.Should().Contain("No migration files are older than the lowest environment version.");
      _reporter.Infos.Should().NotContain("No migration files were deleted.");
      _reporter.Infos.Should().NotContain(info => info.StartsWith("Kept ", StringComparison.Ordinal));
   }

   [Fact]
   public async Task HandleAsync_WithNoEnvironmentsConfigured_WritesErrorAndTouchesNothing()
   {
      var obsolete = CreateMigrationFile("_202602151200_CreateUsers.cs");
      var config = new ToolConfiguration { BasePath = _basePath };

      var exitCode = await CreateHandler(Project(202602151200)).HandleAsync(config, TestContext.Current.CancellationToken);

      exitCode.Should().Be(1);
      _reporter.Errors.Should().ContainInOrder(
         "Error: No environments configured.",
         "Add connectionStrings to .mvdmio-migrations.yml before running cleanup."
      );
      _schemaExportService.ConnectionStrings.Should().BeEmpty();
      File.Exists(obsolete).Should().BeTrue();
   }

   [Fact]
   public async Task HandleAsync_WhenAnExportFails_StopsAtThatEnvironmentDeletesNothingAndFails()
   {
      var obsolete = CreateMigrationFile("_202602151200_CreateUsers.cs");
      _runtimeFactory.RowsByConnectionString[DEV] = Executed(202602151200, 202602170000);
      _runtimeFactory.RowsByConnectionString[PROD] = Executed(202602151200, 202602170000);
      _schemaExportService.FailingConnectionStrings.Add(DEV);

      var exitCode = await CreateHandler(Project(202602151200, 202602170000)).HandleAsync(Config(), TestContext.Current.CancellationToken);

      exitCode.Should().Be(1);
      _reporter.Errors.Should().ContainSingle().Which.Should().Be("Error: Configured schemas were not found in the database: missing_schema.");
      _schemaExportService.ConnectionStrings.Should().Equal(DEV);
      File.Exists(obsolete).Should().BeTrue();
   }

   [Fact]
   public async Task HandleAsync_WhenALaterExportFails_DeletesNothingAndFails()
   {
      var obsolete = CreateMigrationFile("_202602151200_CreateUsers.cs");
      _runtimeFactory.RowsByConnectionString[DEV] = Executed(202602151200, 202602170000);
      _schemaExportService.FailingConnectionStrings.Add(PROD);

      var exitCode = await CreateHandler(Project(202602151200, 202602170000)).HandleAsync(Config(), TestContext.Current.CancellationToken);

      exitCode.Should().Be(1);
      _schemaExportService.ConnectionStrings.Should().Equal(DEV, PROD);
      File.Exists(obsolete).Should().BeTrue();
   }

   [Fact]
   public async Task HandleAsync_WhenAnEnvironmentHasNoRecordedMigration_SkipsCleanupDeletesNothingAndSucceeds()
   {
      var obsolete = CreateMigrationFile("_202602151200_CreateUsers.cs");
      _runtimeFactory.RowsByConnectionString[DEV] = Executed(202602151200, 202602170000);
      _runtimeFactory.RowsByConnectionString[PROD] = Executed(202602151200, 202602170000);
      _schemaExportService.ScriptsByConnectionString[PROD] = "-- no migration header\n";

      var exitCode = await CreateHandler(Project(202602151200, 202602170000)).HandleAsync(Config(), TestContext.Current.CancellationToken);

      exitCode.Should().Be(0);
      _reporter.Infos.Should().Contain("Cleanup skipped: At least one environment has no recorded migration version.");
      _reporter.Errors.Should().BeEmpty();
      File.Exists(obsolete).Should().BeTrue();
   }

   public void Dispose()
   {
      if (Directory.Exists(_basePath))
         Directory.Delete(_basePath, recursive: true);
   }

   private CleanupHandler CreateHandler(MigrationProjectLoader projectLoader)
   {
      return new CleanupHandler(projectLoader, _schemaExportService, _runtimeFactory, _reporter);
   }

   private ToolConfiguration Config()
   {
      return new ToolConfiguration
      {
         BasePath = _basePath,
         ConnectionStrings = new Dictionary<string, string>
         {
            ["dev"] = DEV,
            ["prod"] = PROD
         }
      };
   }

   private string CreateMigrationFile(string fileName)
   {
      var path = Path.Combine(MigrationsDirectory, fileName);
      File.WriteAllText(path, "// test");
      return path;
   }

   private static FakeMigrationProjectLoader Project(params long[] identifiers)
   {
      IDbMigration[] migrations = identifiers.Select(identifier => new FakeMigration(identifier)).ToArray<IDbMigration>();
      return new FakeMigrationProjectLoader
      {
         Result = new MigrationProjectContext(typeof(CleanupHandlerTests).Assembly, new FakeMigrationRetriever(migrations), migrations)
      };
   }

   private static ExecutedMigrationModel[] Executed(params long[] identifiers)
   {
      return identifiers.Select(identifier => new ExecutedMigrationModel(identifier, $"Migration{identifier}", DateTime.UtcNow, SCOPE)).ToArray();
   }

   private sealed class FakeMigrationProjectLoader : MigrationProjectLoader
   {
      public MigrationProjectContext? Result { get; init; }
      public Exception? Failure { get; init; }
      public CancellationToken LoadedWith { get; private set; }

      public override Task<MigrationProjectContext> LoadAsync(string projectPath, CancellationToken cancellationToken)
      {
         LoadedWith = cancellationToken;

         if (Failure is not null)
            throw Failure;

         return Task.FromResult(Result!);
      }
   }

   private sealed class FakeSchemaExportService : SchemaExportService
   {
      public List<string> ConnectionStrings { get; } = [];
      public HashSet<string> FailingConnectionStrings { get; } = new(StringComparer.Ordinal);
      public Dictionary<string, string> ScriptsByConnectionString { get; } = new(StringComparer.Ordinal);

      public override Task<SchemaExportResult> ExportAsync(
         string connectionString,
         IReadOnlyCollection<string>? schemas = null,
         IReadOnlyCollection<string>? ownedScopes = null,
         CancellationToken cancellationToken = default
      )
      {
         ConnectionStrings.Add(connectionString);

         if (FailingConnectionStrings.Contains(connectionString))
            throw new InvalidOperationException("Configured schemas were not found in the database: missing_schema.");

         var script = ScriptsByConnectionString.GetValueOrDefault(connectionString, $"-- Migration version: 202602170000 (Migration202602170000) [{SCOPE}]\n");
         return Task.FromResult(new SchemaExportResult(script, [], [], []));
      }
   }

   private sealed class FakeMigrationRuntimeFactory : IMigrationRuntimeFactory
   {
      public List<string> ConnectionStrings { get; } = [];
      public Dictionary<string, ExecutedMigrationModel[]> RowsByConnectionString { get; } = new(StringComparer.Ordinal);

      public IMigrationRuntime Create(string connectionString, string? environmentName, MigrationProjectContext project)
      {
         ConnectionStrings.Add(connectionString);
         return new FakeMigrationRuntime(RowsByConnectionString.GetValueOrDefault(connectionString, []));
      }
   }

   private sealed class FakeMigrationRuntime(ExecutedMigrationModel[] rows) : IMigrationRuntime
   {
      public ValueTask DisposeAsync()
      {
         return ValueTask.CompletedTask;
      }

      public Task<bool> IsDatabaseEmptyAsync(CancellationToken cancellationToken)
      {
         return Task.FromResult(rows.Length == 0);
      }

      public Task<IEnumerable<ExecutedMigrationModel>> RetrieveAlreadyExecutedMigrationsAsync(CancellationToken cancellationToken)
      {
         return Task.FromResult(rows.AsEnumerable());
      }

      public Task MigrateDatabaseToLatestAsync(DeployPhase phase, CancellationToken cancellationToken)
      {
         throw new NotSupportedException("Cleanup never migrates.");
      }

      public Task MigrateDatabaseToAsync(long targetIdentifier, DeployPhase phase, CancellationToken cancellationToken)
      {
         throw new NotSupportedException("Cleanup never migrates.");
      }
   }

   private sealed class FakeCleanupReporter : ICleanupReporter
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

   private sealed class FakeMigrationRetriever(IReadOnlyList<IDbMigration> migrations) : IMigrationRetriever
   {
      public IEnumerable<IDbMigration> RetrieveMigrations()
      {
         return migrations;
      }
   }

   private sealed class FakeMigration(long identifier) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => $"Migration{identifier}";
      public string Scope => SCOPE;

      public Task UpAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }
}
