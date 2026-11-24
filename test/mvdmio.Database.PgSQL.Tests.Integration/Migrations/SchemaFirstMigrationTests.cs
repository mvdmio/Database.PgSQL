using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema;
using System.Reflection;
using Testcontainers.PostgreSql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

public class SchemaFirstMigrationTests : IAsyncLifetime
{
   private PostgreSqlContainer _dbContainer = null!;
   private DatabaseConnectionFactory _connectionFactory = null!;

   private static Assembly TestAssembly => typeof(SchemaFirstMigrationTests).Assembly;

   private static Assembly SecondaryAssembly => typeof(AssemblyMarker).Assembly;

   protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

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
   public async Task IsDatabaseEmptyAsync_WithNoMigrationsTable_ReturnsTrue()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, typeof(TestFixture).Assembly);

      var isEmpty = await migrator.IsDatabaseEmptyAsync(CancellationToken);

      isEmpty.Should().BeTrue();
   }

   [Fact]
   public async Task IsDatabaseEmptyAsync_WithEmptyMigrationsTable_ReturnsTrue()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      await db.Dapper.ExecuteAsync("""
         CREATE SCHEMA IF NOT EXISTS "mvdmio";
         CREATE TABLE "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );
         """, ct: CancellationToken);

      var migrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, typeof(TestFixture).Assembly);

      var isEmpty = await migrator.IsDatabaseEmptyAsync(CancellationToken);

      isEmpty.Should().BeTrue();
   }

   [Fact]
   public async Task IsDatabaseEmptyAsync_WithMigrationsApplied_ReturnsFalse()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      await db.Dapper.ExecuteAsync("""
         CREATE SCHEMA IF NOT EXISTS "mvdmio";
         CREATE TABLE "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );
         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at)
         VALUES (202505181000, 'TestMigration', NOW());
         """, ct: CancellationToken);

      var migrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, typeof(TestFixture).Assembly);

      var isEmpty = await migrator.IsDatabaseEmptyAsync(CancellationToken);

      isEmpty.Should().BeFalse();
   }

   [Fact]
   public async Task IsDatabaseEmptyAsync_WithOnlyOtherScopeRows_ReturnsFalse()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var firstMigrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, typeof(TestFixture).Assembly);
      await firstMigrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var secondMigrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [SecondaryAssembly],
         new ReflectionMigrationRetriever(SecondaryAssembly));

      var isEmpty = await secondMigrator.IsDatabaseEmptyAsync(CancellationToken);

      isEmpty.Should().BeFalse();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithOtherScopeAlreadyPresent_AppliesSecondarySchemaAndBaseline()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var firstMigrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [TestAssembly],
         new ReflectionMigrationRetriever(typeof(TestFixture).Assembly));
      await firstMigrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var firstScopeRowsBefore = (await firstMigrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken))
         .Where(m => m.Scope == "mvdmio.Database.PgSQL.Tests.Integration")
         .Select(m => (m.Identifier, m.Name))
         .OrderBy(m => m.Identifier)
         .ToArray();

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeFalse();

      var secondMigrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [SecondaryAssembly],
         new ReflectionMigrationRetriever(SecondaryAssembly));
      await secondMigrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_follow_up_table")).Should().BeTrue();

      var executedMigrations = (await secondMigrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m =>
         m.Identifier == 202505181100 &&
         m.Name == "SecondaryTable" &&
         m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");
      executedMigrations.Should().Contain(m =>
         m.Identifier == 202505190000 &&
         m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");
      executedMigrations.Count(m => m.Identifier == 202505181100).Should().Be(1);

      var firstScopeRowsAfter = executedMigrations
         .Where(m => m.Scope == "mvdmio.Database.PgSQL.Tests.Integration")
         .Select(m => (m.Identifier, m.Name))
         .OrderBy(m => m.Identifier)
         .ToArray();
      firstScopeRowsAfter.Should().Equal(firstScopeRowsBefore);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithEmbeddedSchema_AppliesSchemaAndRecordsMigration()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         null,
         NullLoggerFactory.Instance,
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var tableExists = await db.Management.TableExistsAsync("public", "simple_table");
      tableExists.Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithEmbeddedSchema_RecordsBaselineRowWithNullPhase()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         null,
         NullLoggerFactory.Instance,
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var baselinePhases = (await db.Dapper.QueryAsync<string?>(
         """SELECT phase FROM "mvdmio"."migrations" WHERE identifier = 202505181000""",
         ct: CancellationToken)).ToArray();

      baselinePhases.Should().ContainSingle().Which.Should().BeNull();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithEnvironmentSpecificSchema_AppliesCorrectSchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         "local",
         NullLoggerFactory.Instance,
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var tableExists = await db.Management.TableExistsAsync("public", "simple_table");
      tableExists.Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithCaseInsensitiveEnvironment_FindsSchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         "LOCAL",
         NullLoggerFactory.Instance,
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var tableExists = await db.Management.TableExistsAsync("public", "simple_table");
      tableExists.Should().BeTrue();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithDatabaseAlreadyHasMigrations_DoesNotApplyEmbeddedSchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migratorWithoutSchema = new DatabaseMigrator(db, NullLoggerFactory.Instance, typeof(TestFixture).Assembly);
      await migratorWithoutSchema.MigrateDatabaseToLatestAsync(CancellationToken);

      var initialMigrationCount = (await migratorWithoutSchema.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).Count();

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migratorWithSchema = new DatabaseMigrator(
         db,
         null,
         NullLoggerFactory.Instance,
         [TestAssembly],
         migrationRetriever);

      await migratorWithSchema.MigrateDatabaseToLatestAsync(CancellationToken);

      var finalMigrationCount = (await migratorWithSchema.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).Count();
      finalMigrationCount.Should().Be(initialMigrationCount);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithPartialMigrationHistory_SkipsOlderMissingMigrationsAndRunsNewerOnes()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      await db.Dapper.ExecuteAsync(
         """
         CREATE SCHEMA IF NOT EXISTS "mvdmio";
         CREATE TABLE IF NOT EXISTS "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );

         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at)
         VALUES (202505181000, 'SimpleTable', NOW());
         """,
         ct: CancellationToken);

      var migrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, typeof(TestFixture).Assembly);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var legacyTableExists = await db.Management.TableExistsAsync("public", "legacy_cutoff_table");
      legacyTableExists.Should().BeFalse();

      var postCutoffTableExists = await db.Management.TableExistsAsync("public", "post_cutoff_table");
      postCutoffTableExists.Should().BeTrue();

      var complexTableExists = await db.Management.TableExistsAsync("public", "complex_table");
      complexTableExists.Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().HaveCount(3);
      executedMigrations.Should().Contain(x => x.Identifier == 202505181000);
      executedMigrations.Should().Contain(x => x.Identifier == 202505181500);
      executedMigrations.Should().Contain(x => x.Identifier == 202505192230);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithSchemaAndPendingMigrations_AppliesBoth()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         null,
         NullLoggerFactory.Instance,
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var simpleTableExists = await db.Management.TableExistsAsync("public", "simple_table");
      simpleTableExists.Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Length.Should().BeGreaterThan(1);
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
      executedMigrations.Should().Contain(m => m.Identifier == 202505192230);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithNoEmbeddedSchema_RunsMigrationsNormally()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         null,
         NullLoggerFactory.Instance,
         [],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().NotBeEmpty();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithNonExistentEnvironment_FallsBackToDefaultSchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         "nonexistent",
         NullLoggerFactory.Instance,
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var tableExists = await db.Management.TableExistsAsync("public", "simple_table");
      tableExists.Should().BeTrue();
   }

   [Fact]
   public async Task EmbeddedSchemaDiscovery_FindsSchemaResources_InTestAssembly()
   {
      var schemaExists = EmbeddedSchemaDiscovery.SchemaResourceExists([TestAssembly], null);
      schemaExists.Should().BeTrue();

      var localSchemaExists = EmbeddedSchemaDiscovery.SchemaResourceExists([TestAssembly], "local");
      localSchemaExists.Should().BeTrue();
   }

   [Fact]
   public async Task EmbeddedSchemaDiscovery_ReadsSchemaContent_Successfully()
   {
      var content = await EmbeddedSchemaDiscovery.ReadSchemaContentAsync([TestAssembly], null, CancellationToken);

      content.Should().NotBeNullOrEmpty();
      content.Should().Contain("Migration version: 202505181000");
      content.Should().Contain("CREATE TABLE");
   }

   [Fact]
   public async Task EmbeddedSchemaDiscovery_GetSchemaResourceName_ReturnsCorrectName()
   {
      var resourceName = EmbeddedSchemaDiscovery.GetSchemaResourceName([TestAssembly], null);
      resourceName.Should().Be("schema.sql");

      var localResourceName = EmbeddedSchemaDiscovery.GetSchemaResourceName([TestAssembly], "local");
      localResourceName.Should().Be("schema.local.sql");
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithMultipleAssemblies_AppliesAllSchemasInOrder()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [TestAssembly, SecondaryAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000 && m.Name == "SimpleTable" && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      executedMigrations.Should().Contain(m => m.Identifier == 202505181100 && m.Name == "SecondaryTable" && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");
      executedMigrations.Count(m => m.Identifier is 202505181000 or 202505181100).Should().Be(2);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithMultipleAssembliesAndReversedOrder_StillAppliesBoth()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [SecondaryAssembly, TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      executedMigrations.Should().Contain(m => m.Identifier == 202505181100 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithMultipleAssembliesAndEnvironment_AppliesEnvironmentSchemaPerAssembly()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         environment: "local",
         NullLoggerFactory.Instance,
         [TestAssembly, SecondaryAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table_local")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeFalse();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181200 && m.Name == "SecondaryTableLocal" && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");

      executedMigrations.Should().Contain(m => m.Identifier == 202505181000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
   }

   [Fact]
   public void EmbeddedSchemaDiscovery_FindAllSchemaResources_ReturnsOneEntryPerAssembly()
   {
      var resources = EmbeddedSchemaDiscovery.FindAllSchemaResources([TestAssembly, SecondaryAssembly], environment: null);

      try
      {
         resources.Should().HaveCount(2);
         resources[0].Assembly.Should().BeSameAs(TestAssembly);
         resources[1].Assembly.Should().BeSameAs(SecondaryAssembly);
      }
      finally
      {
         foreach (var resource in resources)
            resource.Stream.Dispose();
      }
   }

   [Fact]
   public void EmbeddedSchemaDiscovery_FindAllSchemaResources_SkipsAssembliesWithoutSchema()
   {
      var noSchemaAssembly = typeof(DatabaseMigrator).Assembly;

      var resources = EmbeddedSchemaDiscovery.FindAllSchemaResources(
         [noSchemaAssembly, SecondaryAssembly],
         environment: null);

      try
      {
         resources.Should().HaveCount(1);
         resources[0].Assembly.Should().BeSameAs(SecondaryAssembly);
      }
      finally
      {
         foreach (var resource in resources)
            resource.Stream.Dispose();
      }
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithForeignScopeInSchemaHeader_IgnoresGhostBaselineAndRunsThatScopesMigrations()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var loggerFactory = new CapturingLoggerFactory();
      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly, SecondaryAssembly);
      var migrator = new DatabaseMigrator(
         db,
         environment: "ghostheader",
         loggerFactory,
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();

      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_follow_up_table")).Should().BeTrue();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      executedMigrations.Should().Contain(m => m.Identifier == 202505181100 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");
      executedMigrations.Should().Contain(m => m.Identifier == 202505190000 && m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");

      executedMigrations.Count(m => m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema").Should().Be(2);

      loggerFactory.Entries.Should().ContainSingle(e =>
         e.Level == LogLevel.Warning &&
         e.Message.Contains("mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema") &&
         e.Message.Contains("schema.ghostheader.sql"));
   }

   [Fact]
   public async Task MigrateDatabaseToAsync_WithTargetBelowSecondarySchemaHeader_SkipsOnlyThatSchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly, SecondaryAssembly);
      var migrator = new DatabaseMigrator(
         db,
         environment: null,
         NullLoggerFactory.Instance,
         [TestAssembly, SecondaryAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToAsync(202505181050, CancellationToken);

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeFalse();

      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m =>
         m.Identifier == 202505181000 &&
         m.Scope == "mvdmio.Database.PgSQL.Tests.Integration");
      executedMigrations.Should().NotContain(m =>
         m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema");
   }
}
