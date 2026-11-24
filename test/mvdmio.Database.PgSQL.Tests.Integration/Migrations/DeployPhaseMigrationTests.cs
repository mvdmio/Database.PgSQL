using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Testcontainers.PostgreSql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

public class DeployPhaseMigrationTests : IAsyncLifetime
{
   private const long BASELINE_IDENTIFIER = 202601010001;
   private const long TWO_PART_IDENTIFIER = 202601010002;
   private const long NEWER_IDENTIFIER = 202601010003;

   private PostgreSqlContainer _dbContainer = null!;
   private DatabaseConnectionFactory _connectionFactory = null!;

   private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public async ValueTask InitializeAsync()
   {
      _dbContainer = new PostgreSqlBuilder("postgres:19").Build();
      await _dbContainer.StartAsync();
      _connectionFactory = new DatabaseConnectionFactory();
   }

   public async ValueTask DisposeAsync()
   {
      await _connectionFactory.DisposeAsync();
      await _dbContainer.StopAsync();
      await _dbContainer.DisposeAsync();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithMigrationImplementingUpAsyncOnly_RunsItAndRecordsOnePreDeployRow()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, new FixedMigrationSet([new UpOnlyMigration(BASELINE_IDENTIFIER)]));

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "up_only_table")).Should().BeTrue();

      var phases = (await db.Dapper.QueryAsync<string?>(
         $"""SELECT phase FROM "mvdmio"."migrations" WHERE identifier = {BASELINE_IDENTIFIER}""",
         ct: CancellationToken)).ToArray();
      phases.Should().Equal("pre-deploy");

      var executed = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executed.Should().ContainSingle().Which.Phase.Should().Be(DeployPhase.PreDeploy);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithPreDeployThenPostDeployRuns_RunsEachPartOnceAndRecordsEachRow()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER), new TwoPartMigration(TWO_PART_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      (await CountRowsAsync(db, "phase_check")).Should().Be(0);
      (await HistoryAsync(db)).Should().Equal(
         (BASELINE_IDENTIFIER, "pre-deploy", "UpOnly"),
         (TWO_PART_IDENTIFIER, "pre-deploy", "TwoPart"));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PostDeploy, CancellationToken);

      (await CountRowsAsync(db, "phase_check")).Should().Be(1);
      (await HistoryAsync(db)).Should().Equal(
         (BASELINE_IDENTIFIER, "pre-deploy", "UpOnly"),
         (TWO_PART_IDENTIFIER, "post-deploy", "TwoPart"),
         (TWO_PART_IDENTIFIER, "pre-deploy", "TwoPart"));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PostDeploy, CancellationToken);

      (await CountRowsAsync(db, "phase_check")).Should().Be(1);
      (await HistoryAsync(db)).Should().HaveCount(3);

      var executed = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).Where(x => x.Identifier == TWO_PART_IDENTIFIER);
      executed.Select(x => x.Phase).Should().BeEquivalentTo([DeployPhase.PreDeploy, DeployPhase.PostDeploy]);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithTwoPartMigrationAsTheScopesFirst_RunsItsPostDeployPartInThePostDeployRun()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var logs = new CapturingLoggerFactory();
      var migrator = BuildMigrator(db, logs, new TwoPartMigration(BASELINE_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      (await CountRowsAsync(db, "phase_check")).Should().Be(0);
      (await HistoryAsync(db)).Should().Equal((BASELINE_IDENTIFIER, "pre-deploy", "TwoPart"));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      WarningsOf(logs).Should().ContainSingle().Which.Should().Contain($"{BASELINE_IDENTIFIER} (TwoPart)").And.Contain("is waiting from an earlier run");

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PostDeploy, CancellationToken);

      (await CountRowsAsync(db, "phase_check")).Should().Be(1);
      (await HistoryAsync(db)).Should().Equal(
         (BASELINE_IDENTIFIER, "post-deploy", "TwoPart"),
         (BASELINE_IDENTIFIER, "pre-deploy", "TwoPart"));
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithTwoPartMigrationFoldedIntoSchemaBaselineRow_RunsNeitherPart()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      await MigrationsTableManager.EnsureTableAsync(db, CancellationToken);
      await db.Dapper.ExecuteAsync(
         """
         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at, scope)
         VALUES (202601010001, 'SchemaBaseline', now(), 'DeployPhaseMigrationTests')
         """,
         ct: CancellationToken);
      var migrator = BuildMigrator(db, new TwoPartMigration(BASELINE_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.All, CancellationToken);

      (await db.Management.TableExistsAsync("public", "phase_check")).Should().BeFalse();
      (await HistoryAsync(db)).Should().Equal((BASELINE_IDENTIFIER, null, "SchemaBaseline"));
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithPostDeployPhaseAndPendingPreDeployPart_RunsNothingAndWarnsNamingTheMigration()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var logs = new CapturingLoggerFactory();
      var migrator = BuildMigrator(db, logs, new TwoPartMigration(TWO_PART_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PostDeploy, CancellationToken);

      (await db.Management.TableExistsAsync("public", "phase_check")).Should().BeFalse();
      (await HistoryAsync(db)).Should().BeEmpty();
      WarningsOf(logs).Should().ContainSingle().Which.Should().Contain($"{TWO_PART_IDENTIFIER} (TwoPart)").And.Contain("pre-deploy part is still pending");
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithAllPhaseOnFreshDatabase_RunsBothPartsAndRecordsBothRows()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var logs = new CapturingLoggerFactory();
      var migrator = BuildMigrator(db, logs, new TwoPartMigration(TWO_PART_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.All, CancellationToken);

      (await CountRowsAsync(db, "phase_check")).Should().Be(1);
      (await HistoryAsync(db)).Should().Equal(
         (TWO_PART_IDENTIFIER, "post-deploy", "TwoPart"),
         (TWO_PART_IDENTIFIER, "pre-deploy", "TwoPart"));
      WarningsOf(logs).Should().BeEmpty();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithIndexReplacementRunOutsideTransactions_KeepsBothIndexesUntilThePostDeployRun()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      await db.Dapper.ExecuteAsync("CREATE TABLE public.index_check (value INT NOT NULL)", ct: CancellationToken);
      await db.Dapper.ExecuteAsync("CREATE INDEX index_check_old ON public.index_check (value)", ct: CancellationToken);
      var migrator = BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER), new IndexReplacementMigration(TWO_PART_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      (await IndexNamesAsync(db)).Should().Equal("index_check_new", "index_check_old");

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PostDeploy, CancellationToken);

      (await IndexNamesAsync(db)).Should().Equal("index_check_new");
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithPreDeployRunAfterWaitingPostDeployPart_RunsTheNewerMigrationAndWarnsAboutTheWaitingPart()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      await BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER), new TwoPartMigration(TWO_PART_IDENTIFIER))
         .MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      var logs = new CapturingLoggerFactory();
      var migrator = BuildMigrator(db, logs, new UpOnlyMigration(BASELINE_IDENTIFIER), new TwoPartMigration(TWO_PART_IDENTIFIER), new NewerMigration(NEWER_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      (await db.Management.TableExistsAsync("public", "newer_table")).Should().BeTrue();
      (await CountRowsAsync(db, "phase_check")).Should().Be(0);
      (await HistoryAsync(db)).Select(x => (x.Identifier, x.Phase)).Should().Equal(
         (BASELINE_IDENTIFIER, "pre-deploy"),
         (TWO_PART_IDENTIFIER, "pre-deploy"),
         (NEWER_IDENTIFIER, "pre-deploy"));
      WarningsOf(logs).Should().ContainSingle().Which.Should().Contain($"{TWO_PART_IDENTIFIER} (TwoPart)").And.Contain("waiting");
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithAllPhaseAfterWaitingPostDeployPart_LogsNoWarning()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      await BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER), new TwoPartMigration(TWO_PART_IDENTIFIER))
         .MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      var logs = new CapturingLoggerFactory();
      var migrator = BuildMigrator(db, logs, new UpOnlyMigration(BASELINE_IDENTIFIER), new TwoPartMigration(TWO_PART_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.All, CancellationToken);

      (await CountRowsAsync(db, "phase_check")).Should().Be(1);
      WarningsOf(logs).Should().BeEmpty();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithDeployPhaseOverloads_RunsTheRequestedPartsOnly()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new TwoPartMigration(TWO_PART_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PostDeploy, CancellationToken);

      (await db.Management.TableExistsAsync("public", "phase_check")).Should().BeFalse();

      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      (await db.Management.TableExistsAsync("public", "phase_check")).Should().BeTrue();
      (await CountRowsAsync(db, "phase_check")).Should().Be(0);
      var executed = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executed.Should().ContainSingle().Which.Phase.Should().Be(DeployPhase.PreDeploy);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithoutPhase_RunsPreDeployPartsOnly()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new TwoPartMigration(TWO_PART_IDENTIFIER));

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await HistoryAsync(db)).Should().Equal((TWO_PART_IDENTIFIER, "pre-deploy", "TwoPart"));
      (await CountRowsAsync(db, "phase_check")).Should().Be(0);
   }

   [Fact]
   public async Task MigrateDatabaseToAsync_WithPhaseAndCeiling_RunsTheRequestedPartsUpToTheCeiling()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER), new TwoPartMigration(TWO_PART_IDENTIFIER), new NewerMigration(NEWER_IDENTIFIER));

      await migrator.MigrateDatabaseToAsync(TWO_PART_IDENTIFIER, DeployPhase.All, CancellationToken);

      (await db.Management.TableExistsAsync("public", "newer_table")).Should().BeFalse();
      (await HistoryAsync(db)).Select(x => (x.Identifier, x.Phase)).Should().Equal(
         (BASELINE_IDENTIFIER, "pre-deploy"),
         (TWO_PART_IDENTIFIER, "post-deploy"),
         (TWO_PART_IDENTIFIER, "pre-deploy"));

      await migrator.MigrateDatabaseToAsync(NEWER_IDENTIFIER, CancellationToken);

      (await HistoryAsync(db)).Select(x => (x.Identifier, x.Phase)).Should().HaveCount(4).And.Contain((NEWER_IDENTIFIER, "pre-deploy"));
   }

   [Fact]
   public async Task MigrateDatabaseToAsync_WithPostDeployPhaseAndCeilingBelowWaitingPart_RunsNothing()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER), new TwoPartMigration(TWO_PART_IDENTIFIER));
      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      await migrator.MigrateDatabaseToAsync(BASELINE_IDENTIFIER, DeployPhase.PostDeploy, CancellationToken);

      (await CountRowsAsync(db, "phase_check")).Should().Be(0);
      (await HistoryAsync(db)).Should().HaveCount(2);
   }

   [Theory]
   [InlineData(0)]
   [InlineData(4)]
   [InlineData(-1)]
   [InlineData(8)]
   public async Task EveryOverloadTakingAPhase_WithInvalidPhase_ThrowsArgumentOutOfRangeBeforeAnyConnectionWork(int invalidPhase)
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new TwoPartMigration(TWO_PART_IDENTIFIER));
      var phase = (DeployPhase)invalidPhase;

      var latest = async () => await migrator.MigrateDatabaseToLatestAsync(phase, CancellationToken);
      var to = async () => await migrator.MigrateDatabaseToAsync(TWO_PART_IDENTIFIER, phase, CancellationToken);
      var run = async () => await migrator.RunAsync(new TwoPartMigration(TWO_PART_IDENTIFIER), phase, CancellationToken);

      (await latest.Should().ThrowAsync<ArgumentOutOfRangeException>()).Which.ParamName.Should().Be("phase");
      (await to.Should().ThrowAsync<ArgumentOutOfRangeException>()).Which.ParamName.Should().Be("phase");
      (await run.Should().ThrowAsync<ArgumentOutOfRangeException>()).Which.ParamName.Should().Be("phase");
      (await db.Management.TableExistsAsync("mvdmio", "migrations")).Should().BeFalse();
   }

   [Fact]
   public async Task RunAsync_WithPostDeployPhaseOnMigrationWithoutPostDeployPart_RunsNothingAndWritesNoRow()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER));
      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      await migrator.RunAsync(new NewerMigration(NEWER_IDENTIFIER), DeployPhase.PostDeploy, CancellationToken);

      (await db.Management.TableExistsAsync("public", "newer_table")).Should().BeFalse();
      (await HistoryAsync(db)).Should().Equal((BASELINE_IDENTIFIER, "pre-deploy", "UpOnly"));
   }

   [Fact]
   public async Task RunAsync_WithoutPhase_RunsThePreDeployPartOnly()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER));
      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      await migrator.RunAsync(new TwoPartMigration(TWO_PART_IDENTIFIER), CancellationToken);

      (await HistoryAsync(db)).Should().Contain((TWO_PART_IDENTIFIER, "pre-deploy", "TwoPart")).And.HaveCount(2);
      (await CountRowsAsync(db, "phase_check")).Should().Be(0);
   }

   [Fact]
   public async Task RunAsync_WithAllPhase_RunsBothPartsEachWithItsOwnRow()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER));
      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      await migrator.RunAsync(new TwoPartMigration(TWO_PART_IDENTIFIER), DeployPhase.All, CancellationToken);

      (await HistoryAsync(db)).Should().Equal(
         (BASELINE_IDENTIFIER, "pre-deploy", "UpOnly"),
         (TWO_PART_IDENTIFIER, "post-deploy", "TwoPart"),
         (TWO_PART_IDENTIFIER, "pre-deploy", "TwoPart"));
      (await CountRowsAsync(db, "phase_check")).Should().Be(1);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithFailingPostDeployPartInTransaction_RollsItBackAndKeepsThePreDeployRow()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db, new UpOnlyMigration(BASELINE_IDENTIFIER), new FailingPostDeployMigration(TWO_PART_IDENTIFIER));
      await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PreDeploy, CancellationToken);

      var act = async () => await migrator.MigrateDatabaseToLatestAsync(DeployPhase.PostDeploy, CancellationToken);

      var failure = (await act.Should().ThrowAsync<MigrationException>()).Which;
      failure.Migration.Identifier.Should().Be(TWO_PART_IDENTIFIER);
      failure.InnerException!.Message.Should().Be("The post-deploy part failed.");
      (await CountRowsAsync(db, "failing_post_check")).Should().Be(0);
      (await HistoryAsync(db)).Should().Equal(
         (BASELINE_IDENTIFIER, "pre-deploy", "UpOnly"),
         (TWO_PART_IDENTIFIER, "pre-deploy", "FailingPostDeploy"));
   }

   private static DatabaseMigrator BuildMigrator(DatabaseConnection db, params IDbMigration[] migrations)
   {
      return BuildMigrator(db, NullLoggerFactory.Instance, migrations);
   }

   private static DatabaseMigrator BuildMigrator(DatabaseConnection db, ILoggerFactory loggerFactory, params IDbMigration[] migrations)
   {
      return new DatabaseMigrator(db, loggerFactory, new FixedMigrationSet(migrations));
   }

   private static string[] WarningsOf(CapturingLoggerFactory logs)
   {
      return logs.Entries.Where(x => x.Level == LogLevel.Warning).Select(x => x.Message).ToArray();
   }

   private static async Task<long> CountRowsAsync(DatabaseConnection db, string table)
   {
      return await db.Dapper.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM public.{table}", ct: CancellationToken);
   }

   private static async Task<(long Identifier, string? Phase, string Name)[]> HistoryAsync(DatabaseConnection db)
   {
      if (!await db.Management.TableExistsAsync("mvdmio", "migrations"))
         return [];

      var rows = await db.Dapper.QueryAsync<(long Identifier, string? Phase, string Name)>(
         """SELECT identifier, phase, name FROM "mvdmio"."migrations" ORDER BY identifier, phase""",
         ct: CancellationToken);

      return rows.ToArray();
   }

   private static async Task<string[]> IndexNamesAsync(DatabaseConnection db)
   {
      var names = await db.Dapper.QueryAsync<string>(
         "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'index_check' ORDER BY indexname",
         ct: CancellationToken);

      return names.ToArray();
   }

   private sealed class FixedMigrationSet(IDbMigration[] migrations) : IMigrationRetriever
   {
      public IEnumerable<IDbMigration> RetrieveMigrations()
      {
         return migrations;
      }
   }

#pragma warning disable PGSQL0001 // These helpers set Identifier/Name/Scope explicitly, so the class-name-format analyzer does not apply.
   private sealed class UpOnlyMigration(long identifier) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => "UpOnly";
      public string Scope => "DeployPhaseMigrationTests";

      public Task UpAsync(DatabaseConnection db)
      {
         return db.Dapper.ExecuteAsync("CREATE TABLE public.up_only_table (id INT NOT NULL)");
      }
   }

   private sealed class NewerMigration(long identifier) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => "Newer";
      public string Scope => "DeployPhaseMigrationTests";

      public Task UpAsync(DatabaseConnection db)
      {
         return db.Dapper.ExecuteAsync("CREATE TABLE public.newer_table (id INT NOT NULL)");
      }
   }

   private sealed class TwoPartMigration(long identifier) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => "TwoPart";
      public string Scope => "DeployPhaseMigrationTests";

      public Task PreDeployAsync(DatabaseConnection db)
      {
         return db.Dapper.ExecuteAsync("CREATE TABLE public.phase_check (id INT NOT NULL)");
      }

      public Task PostDeployAsync(DatabaseConnection db)
      {
         return db.Dapper.ExecuteAsync("INSERT INTO public.phase_check (id) VALUES (1)");
      }
   }

   private sealed class IndexReplacementMigration(long identifier) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => "IndexReplacement";
      public string Scope => "DeployPhaseMigrationTests";
      public bool RunsInTransaction => false;

      public Task PreDeployAsync(DatabaseConnection db)
      {
         return db.Dapper.ExecuteAsync("CREATE INDEX CONCURRENTLY index_check_new ON public.index_check (value)");
      }

      public Task PostDeployAsync(DatabaseConnection db)
      {
         return db.Dapper.ExecuteAsync("DROP INDEX CONCURRENTLY public.index_check_old");
      }
   }

   private sealed class FailingPostDeployMigration(long identifier) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => "FailingPostDeploy";
      public string Scope => "DeployPhaseMigrationTests";

      public Task PreDeployAsync(DatabaseConnection db)
      {
         return db.Dapper.ExecuteAsync("CREATE TABLE public.failing_post_check (id INT NOT NULL)");
      }

      public async Task PostDeployAsync(DatabaseConnection db)
      {
         await db.Dapper.ExecuteAsync("INSERT INTO public.failing_post_check (id) VALUES (1)");
         throw new InvalidOperationException("The post-deploy part failed.");
      }
   }
#pragma warning restore PGSQL0001
}
