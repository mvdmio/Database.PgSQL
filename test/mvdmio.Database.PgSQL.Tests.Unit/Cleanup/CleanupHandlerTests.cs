using AwesomeAssertions;
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

      await CreateHandler(Project(202602151200, 202602161000, 202602170000)).HandleAsync(Config(), TestContext.Current.CancellationToken);

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

      await CreateHandler(Project(202602151200, 202602170000)).HandleAsync(Config(), TestContext.Current.CancellationToken);

      File.Exists(current).Should().BeTrue();
      _reporter.Infos.Should().Contain("No migration files are older than the lowest environment version.");
      _reporter.Infos.Should().NotContain("No migration files were deleted.");
      _reporter.Infos.Should().NotContain(info => info.StartsWith("Kept ", StringComparison.Ordinal));
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

      public override MigrationProjectContext Load(string projectPath)
      {
         if (Failure is not null)
            throw Failure;

         return Result!;
      }
   }

   /// <summary>
   ///    Every environment's schema header names the same lowest migration, so the deletion bound is 202602170000.
   /// </summary>
   private sealed class FakeSchemaExportService : SchemaExportService
   {
      public List<string> ConnectionStrings { get; } = [];

      public override Task<SchemaExportResult> ExportAsync(
         string connectionString,
         IReadOnlyCollection<string>? schemas = null,
         IReadOnlyCollection<string>? ownedScopes = null,
         CancellationToken cancellationToken = default
      )
      {
         ConnectionStrings.Add(connectionString);
         return Task.FromResult(new SchemaExportResult($"-- Migration version: 202602170000 (Migration202602170000) [{SCOPE}]\n", [], [], []));
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

      public Task MigrateDatabaseToLatestAsync(CancellationToken cancellationToken)
      {
         throw new NotSupportedException("Cleanup never migrates.");
      }

      public Task MigrateDatabaseToAsync(long targetIdentifier, CancellationToken cancellationToken)
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
