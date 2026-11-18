using AwesomeAssertions;
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

public class ConcurrentMigrationTests : IAsyncLifetime
{
   private const int CONCURRENT_INSTANCES = 3;

   private PostgreSqlContainer _dbContainer = null!;
   private DatabaseConnectionFactory _connectionFactory = null!;

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
   public async Task MigrateDatabaseToLatestAsync_WithConcurrentInstances_AppliesEachMigrationExactlyOnce()
   {
      var connectionString = _dbContainer.GetConnectionString();

      var startSignal = new TaskCompletionSource();

      var runs = Enumerable.Range(0, CONCURRENT_INSTANCES).Select(_ => Task.Run(async () =>
      {
         await using var db = _connectionFactory.BuildConnection(connectionString);
         var migrator = new DatabaseMigrator(db, NullLoggerFactory.Instance, new SlowMigrationSet());

         await startSignal.Task;
         await migrator.MigrateDatabaseToLatestAsync(CancellationToken);
      })).ToArray();

      startSignal.SetResult();

      var act = async () => await Task.WhenAll(runs);
      await act.Should().NotThrowAsync();

      await using var verifyDb = _connectionFactory.BuildConnection(connectionString);

      var executedIdentifiers = (await verifyDb.Dapper.QueryAsync<long>(
         """SELECT identifier FROM "mvdmio"."migrations" ORDER BY identifier""",
         ct: CancellationToken
      )).ToArray();

      executedIdentifiers.Should().Equal(SlowMigrationSet.Identifiers);

      (await verifyDb.Management.TableExistsAsync("public", "concurrent_table_one")).Should().BeTrue();
      (await verifyDb.Management.TableExistsAsync("public", "concurrent_table_slow")).Should().BeTrue();
      (await verifyDb.Management.TableExistsAsync("public", "concurrent_table_three")).Should().BeTrue();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithConcurrentSecondScopeOnPopulatedDatabase_AppliesBaselineOnce()
   {
      var connectionString = _dbContainer.GetConnectionString();

      await using (var seedDb = _connectionFactory.BuildConnection(connectionString))
      {
         var seedMigrator = new DatabaseMigrator(seedDb, NullLoggerFactory.Instance, typeof(TestFixture).Assembly);
         await seedMigrator.MigrateDatabaseToLatestAsync(CancellationToken);
      }

      var startSignal = new TaskCompletionSource();

      var runs = Enumerable.Range(0, CONCURRENT_INSTANCES).Select(_ => Task.Run(async () =>
      {
         await using var db = _connectionFactory.BuildConnection(connectionString);
         var migrator = new DatabaseMigrator(
            db,
            environment: null,
            NullLoggerFactory.Instance,
            [SecondaryAssembly],
            new ReflectionMigrationRetriever(SecondaryAssembly));

         await startSignal.Task;
         await migrator.MigrateDatabaseToLatestAsync(CancellationToken);
      })).ToArray();

      startSignal.SetResult();

      var act = async () => await Task.WhenAll(runs);
      await act.Should().NotThrowAsync();

      await using var verifyDb = _connectionFactory.BuildConnection(connectionString);
      var verifyMigrator = new DatabaseMigrator(
         verifyDb,
         environment: null,
         NullLoggerFactory.Instance,
         [SecondaryAssembly],
         new ReflectionMigrationRetriever(SecondaryAssembly));

      (await verifyDb.Management.TableExistsAsync("public", "secondary_table")).Should().BeTrue();
      (await verifyDb.Management.TableExistsAsync("public", "secondary_follow_up_table")).Should().BeTrue();

      var executedMigrations = (await verifyMigrator.RetrieveAlreadyExecutedMigrationsAsync(CancellationToken)).ToArray();
      executedMigrations.Count(m =>
            m.Identifier == 202505181100 &&
            m.Name == "SecondaryTable" &&
            m.Scope == "mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema")
         .Should().Be(1);
   }

   private sealed class SlowMigrationSet : IMigrationRetriever
   {
      public static readonly long[] Identifiers = [202601010001, 202601010002, 202601010003];

      public IEnumerable<IDbMigration> RetrieveMigrations()
      {
         return
         [
            new CreateTableMigration(202601010001, "ConcurrentTableOne", "concurrent_table_one"),
            new SlowCreateTableMigration(202601010002, "ConcurrentTableSlow", "concurrent_table_slow"),
            new CreateTableMigration(202601010003, "ConcurrentTableThree", "concurrent_table_three")
         ];
      }
   }

#pragma warning disable PGSQL0001 // These helpers set Identifier/Name explicitly, so the class-name-format analyzer does not apply.
   private sealed class CreateTableMigration(long identifier, string name, string tableName) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => name;

      public async Task UpAsync(DatabaseConnection db)
      {
         await db.Dapper.ExecuteAsync($"CREATE TABLE public.{tableName} (id BIGINT NOT NULL PRIMARY KEY)");
      }
   }

   private sealed class SlowCreateTableMigration(long identifier, string name, string tableName) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => name;

      public async Task UpAsync(DatabaseConnection db)
      {
         await db.Dapper.ExecuteAsync("SELECT pg_sleep(1)");
         await db.Dapper.ExecuteAsync($"CREATE TABLE public.{tableName} (id BIGINT NOT NULL PRIMARY KEY)");
      }
   }
#pragma warning restore PGSQL0001
}
