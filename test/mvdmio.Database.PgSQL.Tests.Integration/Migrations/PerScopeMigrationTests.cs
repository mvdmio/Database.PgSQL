using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema;
using System.Reflection;
using Testcontainers.PostgreSql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

/// <summary>
///    End-to-end tests for per-scope migration watermarks: two assemblies migrating the same database advance
///    independently, legacy scope-less tables are upgraded and backfilled in place, and unattributed rows
///    warn instead of silently suppressing migrations. These tests use their own PostgreSQL container.
/// </summary>
public class PerScopeMigrationTests : IAsyncLifetime
{
   private PostgreSqlContainer _dbContainer = null!;
   private DatabaseConnectionFactory _connectionFactory = null!;

   private static Assembly TestAssembly => typeof(PerScopeMigrationTests).Assembly;
   private static Assembly SecondaryAssembly => typeof(AssemblyMarker).Assembly;

   protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   public async ValueTask InitializeAsync()
   {
      _dbContainer = new PostgreSqlBuilder("postgres:18").Build();
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
   public async Task MigrateDatabaseToLatestAsync_WithSecondScopeBehindFirstScopesWatermark_RunsAllOfSecondScopesMigrations()
   {
      // The core regression: scope A has already advanced to a high identifier. Scope B's migrations carry
      // lower identifiers; a global watermark would silently skip them, per-scope watermarks must not.
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var scopeARetriever = new FixedMigrationSet(
         new CreateTableMigration(202606010000, "ScopeATable", "ScopeA", "scope_a_table"));
      var migratorA = new DatabaseMigrator(db, NullLoggerFactory.Instance, scopeARetriever);
      await migratorA.MigrateDatabaseToLatestAsync(CancellationToken);

      var scopeBRetriever = new FixedMigrationSet(
         new CreateTableMigration(202601010000, "ScopeBTableOne", "ScopeB", "scope_b_table_one"),
         new CreateTableMigration(202602010000, "ScopeBTableTwo", "ScopeB", "scope_b_table_two"));
      var migratorB = new DatabaseMigrator(db, NullLoggerFactory.Instance, scopeBRetriever);
      await migratorB.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "scope_a_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "scope_b_table_one")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "scope_b_table_two")).Should().BeTrue();

      var executedMigrations = (await migratorB.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202601010000 && m.Scope == "ScopeB");
      executedMigrations.Should().Contain(m => m.Identifier == 202602010000 && m.Scope == "ScopeB");
      executedMigrations.Should().Contain(m => m.Identifier == 202606010000 && m.Scope == "ScopeA");
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_OnLegacyDatabase_UpgradesTableAndBackfillsScopes()
   {
      // An existing populated database created by the previous major version: legacy table shape and
      // scope-less rows. The next run must upgrade the table, attribute the rows, and not re-run anything.
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      await db.Dapper.ExecuteAsync(
         """
         CREATE SCHEMA IF NOT EXISTS "mvdmio";
         CREATE TABLE "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );

         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at)
         VALUES (202505181000, 'SimpleTable', NOW()), (202505181500, 'PostCutoffTable', NOW());

         CREATE TABLE public.simple_table (id BIGINT NOT NULL PRIMARY KEY);
         CREATE TABLE public.post_cutoff_table (id BIGINT NOT NULL PRIMARY KEY);
         """,
         ct: CancellationToken);

      var migrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, new ReflectionMigrationRetriever(typeof(TestFixture).Assembly));

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();

      // Legacy rows are attributed to the scope of the discovered migrations with matching identifiers.
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      executedMigrations.Should().Contain(m => m.Identifier == 202505181500 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");

      // Already-applied migrations are not re-run; the one pending migration is.
      executedMigrations.Should().HaveCount(3);
      executedMigrations.Should().Contain(m => m.Identifier == 202505192230 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      (await db.Management.TableExistsAsync("public", "complex_table")).Should().BeTrue();

      // The table itself is upgraded: no primary key, scope column present.
      var primaryKeyCount = await db.Dapper.ExecuteScalarAsync<long>(
         """
         SELECT COUNT(*)
         FROM pg_constraint c
         JOIN pg_class t ON c.conrelid = t.oid
         JOIN pg_namespace n ON t.relnamespace = n.oid
         WHERE n.nspname = 'mvdmio' AND t.relname = 'migrations' AND c.contype = 'p'
         """,
         ct: CancellationToken);
      primaryKeyCount.Should().Be(0);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithUnattributableLegacyRow_WarnsAndDoesNotSuppressOtherScopes()
   {
      // A legacy row no discovered migration claims must stay scope-less, emit one warning, and must not act
      // as a watermark for any scope: migrations with lower identifiers still run.
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      await db.Dapper.ExecuteAsync(
         """
         CREATE SCHEMA IF NOT EXISTS "mvdmio";
         CREATE TABLE "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );

         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at)
         VALUES (202606010000, 'UnknownMigration', NOW());
         """,
         ct: CancellationToken);

      var loggerFactory = new CapturingLoggerFactory();
      var retriever = new FixedMigrationSet(
         new CreateTableMigration(202601010000, "ScopeATable", "ScopeA", "scope_a_table"));
      var migrator = new DatabaseMigrator(db, loggerFactory, retriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // The lower-identifier migration ran despite the higher unattributed row.
      (await db.Management.TableExistsAsync("public", "scope_a_table")).Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202606010000 && m.Scope == null);
      executedMigrations.Should().Contain(m => m.Identifier == 202601010000 && m.Scope == "ScopeA");

      loggerFactory.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("202606010000"));
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithMultipleSchemaFirstAssemblies_RecordsBaselinePerScopeAndRunsPostBaselineMigrations()
   {
      // A single migrator bootstrapping an empty database from two schema-first assemblies: every assembly's
      // schema is applied, each scope gets its own baseline, and each scope's post-baseline migrations run.
      // The secondary assembly's 202505181100 migration mirrors its schema baseline: if the baseline did not
      // suppress it, the CREATE TABLE would collide with the schema-created table and the run would fail.
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly, SecondaryAssembly);
      var migrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [TestAssembly, SecondaryAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // Tables from both schemas exist.
      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeTrue();

      // Each scope's post-baseline migrations ran.
      (await db.Management.TableExistsAsync("public", "post_cutoff_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "complex_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_follow_up_table")).Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      executedMigrations.Should().Contain(m => m.Identifier == 202505181100 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");
      executedMigrations.Should().Contain(m => m.Identifier == 202505190000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");

      // The baseline row covers the folded-in 202505181100 migration: exactly one row carries that identifier.
      executedMigrations.Count(m => m.Identifier == 202505181100).Should().Be(1);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithMultipleLegacyScopelessSchemaHeaders_RecordsABaselinePerHeaderAndHealsEach()
   {
      // Both assemblies' schema.legacymulti.sql files carry legacy scope-less headers (1000 and 1100).
      // Each header must be recorded as its own baseline row — collapsing them to the highest would leave
      // the primary scope without a watermark, and its folded-in SimpleTable migration would re-run against
      // the schema-created table. The backfill then attributes each baseline to its scope by identifier.
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly, SecondaryAssembly);
      var migrator = new DatabaseMigrator(
         db,
         environment: "legacymulti",
         NullLoggerFactory.Instance,
         [TestAssembly, SecondaryAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      executedMigrations.Should().Contain(m => m.Identifier == 202505181100 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");

      // Post-baseline migrations of both scopes ran; the folded-in ones did not re-run.
      (await db.Management.TableExistsAsync("public", "complex_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_follow_up_table")).Should().BeTrue();
      executedMigrations.Count(m => m.Identifier is 202505181000 or 202505181100).Should().Be(2);
   }

   [Fact]
   public async Task MigrateDatabaseToAsync_WithTwoScopes_AdvancesEveryScopeUpToTheTarget()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var retriever = new FixedMigrationSet(
         new CreateTableMigration(202601010000, "ScopeAOne", "ScopeA", "scope_a_one"),
         new CreateTableMigration(202603010000, "ScopeATwo", "ScopeA", "scope_a_two"),
         new CreateTableMigration(202602010000, "ScopeBOne", "ScopeB", "scope_b_one"),
         new CreateTableMigration(202604010000, "ScopeBTwo", "ScopeB", "scope_b_two"));
      var migrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, retriever);

      await migrator.MigrateDatabaseToAsync(202603010000, CancellationToken);

      (await db.Management.TableExistsAsync("public", "scope_a_one")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "scope_a_two")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "scope_b_one")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "scope_b_two")).Should().BeFalse();
   }

   private sealed class FixedMigrationSet : IMigrationRetriever
   {
      private readonly IDbMigration[] _migrations;

      public FixedMigrationSet(params IDbMigration[] migrations)
      {
         _migrations = migrations;
      }

      public IEnumerable<IDbMigration> RetrieveMigrations()
      {
         return _migrations;
      }
   }

   // These helpers set Identifier/Name/Scope explicitly, so the class-name-format analyzer does not apply.
#pragma warning disable PGSQL0001
   private sealed class CreateTableMigration(long identifier, string name, string scope, string tableName) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => name;
      public string Scope => scope;

      public async Task UpAsync(DatabaseConnection db)
      {
         await db.Dapper.ExecuteAsync($"CREATE TABLE public.{tableName} (id BIGINT NOT NULL PRIMARY KEY)");
      }
   }
#pragma warning restore PGSQL0001

   private sealed class CapturingLoggerFactory : ILoggerFactory
   {
      private readonly CapturingLogger _logger = new();

      public List<(LogLevel Level, string Message)> Entries => _logger.Entries;

      public ILogger CreateLogger(string categoryName)
      {
         return _logger;
      }

      public void AddProvider(ILoggerProvider provider)
      {
         // No-op.
      }

      public void Dispose()
      {
      }
   }

   private sealed class CapturingLogger : ILogger
   {
      public List<(LogLevel Level, string Message)> Entries { get; } = [];

      public IDisposable? BeginScope<TState>(TState state)
         where TState : notnull
      {
         return null;
      }

      public bool IsEnabled(LogLevel logLevel)
      {
         return true;
      }

      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
      {
         Entries.Add((logLevel, formatter(state, exception)));
      }
   }
}
