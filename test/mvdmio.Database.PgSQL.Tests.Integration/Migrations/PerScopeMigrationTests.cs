using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema;
using System.Reflection;
using Testcontainers.PostgreSql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

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

      executedMigrations.Should().Contain(m => m.Identifier == 202505181000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      executedMigrations.Should().Contain(m => m.Identifier == 202505181500 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");

      executedMigrations.Should().HaveCount(3);
      executedMigrations.Should().Contain(m => m.Identifier == 202505192230 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      (await db.Management.TableExistsAsync("public", "complex_table")).Should().BeTrue();

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

      (await db.Management.TableExistsAsync("public", "scope_a_table")).Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202606010000 && m.Scope == null);
      executedMigrations.Should().Contain(m => m.Identifier == 202601010000 && m.Scope == "ScopeA");

      loggerFactory.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("202606010000"));
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithMultipleSchemaFirstAssemblies_RecordsBaselinePerScopeAndRunsPostBaselineMigrations()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly, SecondaryAssembly);
      var migrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [TestAssembly, SecondaryAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeTrue();

      (await db.Management.TableExistsAsync("public", "post_cutoff_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "complex_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_follow_up_table")).Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      executedMigrations.Should().Contain(m => m.Identifier == 202505181100 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");
      executedMigrations.Should().Contain(m => m.Identifier == 202505190000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");

      executedMigrations.Count(m => m.Identifier == 202505181100).Should().Be(1);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithOneScopeAlreadyPresent_AppliesOnlyTheEmptyAssemblySchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var firstMigrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [TestAssembly],
         new ReflectionMigrationRetriever(typeof(TestFixture).Assembly));
      await firstMigrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var firstScopeCountBefore = (await firstMigrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken))
         .Count(m => m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");

      var combinedMigrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [TestAssembly, SecondaryAssembly],
         new ReflectionMigrationRetriever(typeof(TestFixture).Assembly, SecondaryAssembly));
      await combinedMigrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_follow_up_table")).Should().BeTrue();

      var executedMigrations = (await combinedMigrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Count(m => m.Scope == "mvdmio.Database.PgSQL.Tests.Integration").Should().Be(firstScopeCountBefore);
      executedMigrations.Should().Contain(m =>
         m.Identifier == 202505181100 &&
         m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");
      executedMigrations.Count(m => m.Identifier == 202505181100).Should().Be(1);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithMultipleLegacyScopelessSchemaHeaders_RecordsABaselinePerHeaderAndHealsEach()
   {
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

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithOutOfOrderMigration_RunsItLateAndWarnsOnce()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var scopeAOne = new CreateTableMigration(202601010000, "ScopeAOne", "ScopeA", "scope_a_one");
      var scopeAThree = new CreateTableMigration(202603010000, "ScopeAThree", "ScopeA", "scope_a_three");
      var lateMergedScopeATwo = new CreateTableMigration(202602010000, "ScopeATwo", "ScopeA", "scope_a_two");

      var firstLoggerFactory = new CapturingLoggerFactory();
      await new DatabaseMigrator(db, firstLoggerFactory, new FixedMigrationSet(scopeAOne, scopeAThree)).MigrateDatabaseToLatestAsync(CancellationToken);

      var loggerFactory = new CapturingLoggerFactory();
      var retriever = new FixedMigrationSet(scopeAOne, lateMergedScopeATwo, scopeAThree);
      var migrator = new DatabaseMigrator(db, loggerFactory, retriever);
      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "scope_a_two")).Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().ContainSingle(m => m.Identifier == 202602010000 && m.Scope == "ScopeA");

      firstLoggerFactory.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
      var warning = loggerFactory.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
      warning.Message.Should().Contain("ScopeA").And.Contain("202602010000").And.Contain("ScopeATwo").And.Contain("202603010000");
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithFoldedMigrationBelowBootstrapBaseline_DoesNotRunIt()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrations = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly).RetrieveMigrations()
         .Append(new CreateTableMigration(202505170000, "FoldedTable", "mvdmio.Database.PgSQL.Tests.Integration", "folded_table"))
         .ToArray();
      var loggerFactory = new CapturingLoggerFactory();

      for (var run = 0; run < 2; run++)
      {
         var migrator = new DatabaseMigrator(db, environment: null, loggerFactory, [TestAssembly], new FixedMigrationSet(migrations));
         await migrator.MigrateDatabaseToLatestAsync(CancellationToken);
      }

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "complex_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "folded_table")).Should().BeFalse();
      loggerFactory.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithFailingOutOfOrderMigration_ThrowsAndKeepsEarlierMigrations()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var scopeAOne = new CreateTableMigration(202601010000, "ScopeAOne", "ScopeA", "scope_a_one");
      var scopeAFive = new CreateTableMigration(202605010000, "ScopeAFive", "ScopeA", "scope_a_five");
      await new DatabaseMigrator(db, NullLoggerFactory.Instance, new FixedMigrationSet(scopeAOne, scopeAFive)).MigrateDatabaseToLatestAsync(CancellationToken);

      var retriever = new FixedMigrationSet(
         scopeAOne,
         new CreateTableMigration(202602010000, "ScopeATwo", "ScopeA", "scope_a_two"),
         new CreateTableMigration(202603010000, "ScopeAThreeConflicts", "ScopeA", "scope_a_five"),
         scopeAFive,
         new CreateTableMigration(202606010000, "ScopeASix", "ScopeA", "scope_a_six"));
      var migrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, retriever);

      var act = () => migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var exception = await act.Should().ThrowAsync<MigrationException>();
      exception.Which.Migration.Identifier.Should().Be(202603010000);

      (await db.Management.TableExistsAsync("public", "scope_a_two")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "scope_a_six")).Should().BeFalse();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202602010000 && m.Scope == "ScopeA");
      executedMigrations.Should().NotContain(m => m.Identifier == 202603010000);
      executedMigrations.Should().NotContain(m => m.Identifier == 202606010000);
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

#pragma warning disable PGSQL0001 // These helpers set Identifier/Name/Scope explicitly, so the class-name-format analyzer does not apply.
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

}
