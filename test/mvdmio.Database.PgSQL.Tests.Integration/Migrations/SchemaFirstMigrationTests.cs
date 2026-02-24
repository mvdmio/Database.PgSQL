using System.Reflection;
using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Testcontainers.PostgreSql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

/// <summary>
///    Tests for schema-first migration functionality using embedded schema resources.
///    These tests use their own PostgreSQL container without pre-applied migrations.
/// </summary>
public class SchemaFirstMigrationTests : IAsyncLifetime
{
   private PostgreSqlContainer _dbContainer = null!;
   private DatabaseConnectionFactory _connectionFactory = null!;

   /// <summary>
   ///    Gets the test assembly which contains embedded schema resources.
   /// </summary>
   private static Assembly TestAssembly => typeof(SchemaFirstMigrationTests).Assembly;

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
   public async Task IsDatabaseEmptyAsync_WithNoMigrationsTable_ReturnsTrue()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = new DatabaseMigrator(db, typeof(TestFixture).Assembly);

      var isEmpty = await migrator.IsDatabaseEmptyAsync(CancellationToken);

      isEmpty.Should().BeTrue();
   }

   [Fact]
   public async Task IsDatabaseEmptyAsync_WithEmptyMigrationsTable_ReturnsTrue()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      // Create the migrations table but don't add any entries
      await db.Dapper.ExecuteAsync("""
         CREATE SCHEMA IF NOT EXISTS "mvdmio";
         CREATE TABLE "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );
         """, ct: CancellationToken);

      var migrator = new DatabaseMigrator(db, typeof(TestFixture).Assembly);

      var isEmpty = await migrator.IsDatabaseEmptyAsync(CancellationToken);

      isEmpty.Should().BeTrue();
   }

   [Fact]
   public async Task IsDatabaseEmptyAsync_WithMigrationsApplied_ReturnsFalse()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      // Create the migrations table and add an entry
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

      var migrator = new DatabaseMigrator(db, typeof(TestFixture).Assembly);

      var isEmpty = await migrator.IsDatabaseEmptyAsync(CancellationToken);

      isEmpty.Should().BeFalse();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithEmbeddedSchema_AppliesSchemaAndRecordsMigration()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      // Use the embedded schema.sql from the test assembly
      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         null, // Will pick up schema.sql
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // Verify the table was created (from schema)
      var tableExists = await db.Management.TableExistsAsync("public", "simple_table");
      tableExists.Should().BeTrue();

      // Verify the migration was recorded
      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithEnvironmentSpecificSchema_AppliesCorrectSchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      // Use the embedded schema.local.sql from the test assembly
      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         "local", // Will pick up schema.local.sql
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // Verify the table was created
      var tableExists = await db.Management.TableExistsAsync("public", "simple_table");
      tableExists.Should().BeTrue();

      // Verify the migration was recorded
      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithCaseInsensitiveEnvironment_FindsSchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      // Use uppercase environment name - should still find schema.local.sql
      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         "LOCAL", // Case-insensitive lookup
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // Verify the table was created
      var tableExists = await db.Management.TableExistsAsync("public", "simple_table");
      tableExists.Should().BeTrue();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithDatabaseAlreadyHasMigrations_DoesNotApplyEmbeddedSchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      // First apply some migrations normally (without embedded schema)
      var migratorWithoutSchema = new DatabaseMigrator(db, typeof(TestFixture).Assembly);
      await migratorWithoutSchema.MigrateDatabaseToLatestAsync(CancellationToken);

      var initialMigrationCount = (await migratorWithoutSchema.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).Count();

      // Create a new migrator with embedded schema discovery
      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migratorWithSchema = new DatabaseMigrator(
         db,
         null,
         [TestAssembly],
         migrationRetriever);

      // This should NOT re-apply the schema since database already has migrations
      await migratorWithSchema.MigrateDatabaseToLatestAsync(CancellationToken);

      // Migration count should be unchanged
      var finalMigrationCount = (await migratorWithSchema.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).Count();
      finalMigrationCount.Should().Be(initialMigrationCount);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithSchemaAndPendingMigrations_AppliesBoth()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      // The embedded schema contains migration 202505181000 (SimpleTable)
      // The TestFixture assembly has migrations 202505181000 and 202505192230 (ComplexTable)
      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         null,
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // Verify the simple_table was created (from schema)
      var simpleTableExists = await db.Management.TableExistsAsync("public", "simple_table");
      simpleTableExists.Should().BeTrue();

      // Verify all migrations are now recorded (schema + subsequent migrations)
      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Length.Should().BeGreaterThan(1);
      executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
      executedMigrations.Should().Contain(m => m.Identifier == 202505192230);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithNoEmbeddedSchema_RunsMigrationsNormally()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      // Pass an empty array for schema assemblies - no embedded schema will be found
      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         null,
         [], // No assemblies for schema discovery
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // Verify migrations were applied
      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().NotBeEmpty();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithNonExistentEnvironment_FallsBackToDefaultSchema()
   {
      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());

      // Use an environment that doesn't have a specific schema file
      var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
      var migrator = new DatabaseMigrator(
         db,
         "nonexistent", // No schema.nonexistent.sql exists
         [TestAssembly],
         migrationRetriever);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // Should fall back to schema.sql and create the table
      var tableExists = await db.Management.TableExistsAsync("public", "simple_table");
      tableExists.Should().BeTrue();
   }

   [Fact]
   public async Task EmbeddedSchemaDiscovery_FindsSchemaResources_InTestAssembly()
   {
      // Verify that the test assembly has embedded schema resources
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
}
