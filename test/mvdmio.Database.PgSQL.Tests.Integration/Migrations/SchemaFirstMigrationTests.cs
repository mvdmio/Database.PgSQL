using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Testcontainers.PostgreSql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

/// <summary>
///    Tests for schema-first migration functionality.
///    These tests use their own PostgreSQL container without pre-applied migrations.
/// </summary>
public class SchemaFirstMigrationTests : IAsyncLifetime
{
   private PostgreSqlContainer _dbContainer = null!;
   private DatabaseConnectionFactory _connectionFactory = null!;

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
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());
      var migrator = new DatabaseMigrator(db, typeof(TestFixture).Assembly);

      var isEmpty = await migrator.IsDatabaseEmptyAsync(CancellationToken);

      isEmpty.Should().BeTrue();
   }

   [Fact]
   public async Task IsDatabaseEmptyAsync_WithEmptyMigrationsTable_ReturnsTrue()
   {
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

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
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

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
   public async Task MigrateDatabaseToLatestAsync_WithSchemaFile_AppliesSchemaAndRecordsMigration()
   {
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

      // Create a temporary schema file
      var schemaFilePath = Path.GetTempFileName();

      try
      {
         await File.WriteAllTextAsync(schemaFilePath, """
            --
            -- PostgreSQL database schema
            -- Generated at 2026-02-18 10:30:45 UTC
            -- Migration version: 202505181000 (SimpleTable)
            --

            CREATE SCHEMA IF NOT EXISTS "mvdmio";
            CREATE TABLE "mvdmio"."migrations" (
               identifier  BIGINT      NOT NULL,
               name        TEXT        NOT NULL,
               executed_at TIMESTAMPTZ NOT NULL,
               PRIMARY KEY (identifier)
            );

            CREATE TABLE public.simple_table (
                id                    BIGINT NOT NULL,
                required_string_value TEXT   NOT NULL,
                optional_string_value TEXT   NULL,
                PRIMARY KEY (id)
            );
            """, CancellationToken);

         var migrator = new DatabaseMigrator(db, MigrationTableConfiguration.Default, schemaFilePath, typeof(TestFixture).Assembly);

         await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

         // Verify the table was created
         var tableExists = await db.Management.TableExistsAsync("public", "simple_table");
         tableExists.Should().BeTrue();

         // Verify the migration was recorded
         var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
         executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
      }
      finally
      {
         File.Delete(schemaFilePath);
      }
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithDatabaseAlreadyHasMigrations_DoesNotApplySchemaFile()
   {
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

      // First apply some migrations normally (without schema file)
      var migratorWithoutSchema = new DatabaseMigrator(db, typeof(TestFixture).Assembly);
      await migratorWithoutSchema.MigrateDatabaseToLatestAsync(CancellationToken);

      var initialMigrationCount = (await migratorWithoutSchema.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).Count();

      // Create a schema file that would create a different table
      var schemaFilePath = Path.GetTempFileName();

      try
      {
         await File.WriteAllTextAsync(schemaFilePath, """
            --
            -- PostgreSQL database schema
            -- Migration version: 202505181000 (SimpleTable)
            --
            CREATE TABLE public.schema_only_table (id INT);
            """, CancellationToken);

         // Create a new migrator with schema file
         var migratorWithSchema = new DatabaseMigrator(db, MigrationTableConfiguration.Default, schemaFilePath, typeof(TestFixture).Assembly);

         // This should NOT apply the schema file since database already has migrations
         await migratorWithSchema.MigrateDatabaseToLatestAsync(CancellationToken);

         // Verify the schema-only table was NOT created
         var schemaOnlyTableExists = await db.Management.TableExistsAsync("public", "schema_only_table");
         schemaOnlyTableExists.Should().BeFalse();

         // Migration count should be unchanged
         var finalMigrationCount = (await migratorWithSchema.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).Count();
         finalMigrationCount.Should().Be(initialMigrationCount);
      }
      finally
      {
         File.Delete(schemaFilePath);
      }
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithSchemaAndPendingMigrations_AppliesBoth()
   {
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

      // Create a schema file that represents only the first migration
      var schemaFilePath = Path.GetTempFileName();

      try
      {
         await File.WriteAllTextAsync(schemaFilePath, """
            --
            -- PostgreSQL database schema
            -- Generated at 2026-02-18 10:30:45 UTC
            -- Migration version: 202505181000 (SimpleTable)
            --

            CREATE SCHEMA IF NOT EXISTS "mvdmio";
            CREATE TABLE "mvdmio"."migrations" (
               identifier  BIGINT      NOT NULL,
               name        TEXT        NOT NULL,
               executed_at TIMESTAMPTZ NOT NULL,
               PRIMARY KEY (identifier)
            );

            CREATE TABLE public.simple_table (
                id                    BIGINT NOT NULL,
                required_string_value TEXT   NOT NULL,
                optional_string_value TEXT   NULL,
                PRIMARY KEY (id)
            );
            """, CancellationToken);

         var migrator = new DatabaseMigrator(db, MigrationTableConfiguration.Default, schemaFilePath, typeof(TestFixture).Assembly);

         await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

         // Verify the simple_table was created (from schema)
         var simpleTableExists = await db.Management.TableExistsAsync("public", "simple_table");
         simpleTableExists.Should().BeTrue();

         // Verify all migrations are now recorded (schema + subsequent migrations)
         var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
         executedMigrations.Length.Should().BeGreaterThan(1);
         executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
      }
      finally
      {
         File.Delete(schemaFilePath);
      }
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithNoMigrationVersionInSchemaHeader_AppliesSchemaWithoutRecordingMigration()
   {
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

      // Create a schema file without migration version
      var schemaFilePath = Path.GetTempFileName();

      try
      {
         await File.WriteAllTextAsync(schemaFilePath, """
            --
            -- PostgreSQL database schema
            -- Generated at 2026-02-18 10:30:45 UTC
            -- Migration version: (none)
            --

            CREATE SCHEMA IF NOT EXISTS "mvdmio";
            CREATE TABLE "mvdmio"."migrations" (
               identifier  BIGINT      NOT NULL,
               name        TEXT        NOT NULL,
               executed_at TIMESTAMPTZ NOT NULL,
               PRIMARY KEY (identifier)
            );

            CREATE TABLE public.test_table (
                id BIGINT NOT NULL PRIMARY KEY
            );
            """, CancellationToken);

         var migrationRetriever = new ReflectionMigrationRetriever(typeof(TestFixture).Assembly);
         var migrator = new DatabaseMigrator(db, MigrationTableConfiguration.Default, schemaFilePath, migrationRetriever);

         await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

         // Verify the table was created
         var tableExists = await db.Management.TableExistsAsync("public", "test_table");
         tableExists.Should().BeTrue();

         // Verify subsequent migrations were still applied
         var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
         // Should have all test migrations applied (not the schema version since it was "(none)")
         executedMigrations.Should().NotBeEmpty();
      }
      finally
      {
         File.Delete(schemaFilePath);
      }
   }

   [Fact]
   public async Task MigrateDatabaseToAsync_WithSchemaVersionGreaterThanTarget_DoesNotApplySchema()
   {
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

      // Create a schema file with a version newer than our target
      var schemaFilePath = Path.GetTempFileName();

      try
      {
         await File.WriteAllTextAsync(schemaFilePath, """
            --
            -- PostgreSQL database schema
            -- Migration version: 999999999999 (FutureMigration)
            --
            CREATE TABLE public.future_table (id INT);
            """, CancellationToken);

         var migrator = new DatabaseMigrator(db, MigrationTableConfiguration.Default, schemaFilePath, typeof(TestFixture).Assembly);

         // Target an earlier version than the schema
         await migrator.MigrateDatabaseToAsync(202505181000, CancellationToken);

         // Verify the future_table was NOT created (schema was skipped)
         var futureTableExists = await db.Management.TableExistsAsync("public", "future_table");
         futureTableExists.Should().BeFalse();

         // Verify migrations were run normally
         var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
         executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
      }
      finally
      {
         File.Delete(schemaFilePath);
      }
   }

   [Fact]
   public async Task MigrateDatabaseToAsync_WithSchemaVersionLessThanTarget_AppliesSchemaAndRemainingMigrations()
   {
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

      // Create a schema file with a version less than target
      var schemaFilePath = Path.GetTempFileName();

      try
      {
         await File.WriteAllTextAsync(schemaFilePath, """
            --
            -- PostgreSQL database schema
            -- Migration version: 202505181000 (SimpleTable)
            --

            CREATE SCHEMA IF NOT EXISTS "mvdmio";
            CREATE TABLE "mvdmio"."migrations" (
               identifier  BIGINT      NOT NULL,
               name        TEXT        NOT NULL,
               executed_at TIMESTAMPTZ NOT NULL,
               PRIMARY KEY (identifier)
            );

            CREATE TABLE public.simple_table (
                id                    BIGINT NOT NULL,
                required_string_value TEXT   NOT NULL,
                optional_string_value TEXT   NULL,
                PRIMARY KEY (id)
            );
            """, CancellationToken);

         var migrator = new DatabaseMigrator(db, MigrationTableConfiguration.Default, schemaFilePath, typeof(TestFixture).Assembly);

         // Target a later version than the schema (202505192230 is ComplexTable)
         await migrator.MigrateDatabaseToAsync(202505192230, CancellationToken);

         // Verify the simple_table was created (from schema)
         var simpleTableExists = await db.Management.TableExistsAsync("public", "simple_table");
         simpleTableExists.Should().BeTrue();

         // Verify migrations up to target were applied
         var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
         executedMigrations.Should().Contain(m => m.Identifier == 202505181000);
         executedMigrations.Should().Contain(m => m.Identifier == 202505192230);
      }
      finally
      {
         File.Delete(schemaFilePath);
      }
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithMissingSchemaFile_RunsMigrationsNormally()
   {
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

      var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sql");

      // Create migrator with non-existent schema file
      var migrator = new DatabaseMigrator(db, MigrationTableConfiguration.Default, nonExistentPath, typeof(TestFixture).Assembly);

      // Should run migrations normally without throwing
      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // Verify migrations were applied
      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().NotBeEmpty();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithNoSchemaFilePath_RunsMigrationsNormally()
   {
      await using var db = _connectionFactory.ForConnectionString(_dbContainer.GetConnectionString());

      // Create migrator without schema file (null)
      var migrator = new DatabaseMigrator(db, typeof(TestFixture).Assembly);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      // Verify migrations were applied
      var executedMigrations = (await migrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Should().NotBeEmpty();
   }
}
