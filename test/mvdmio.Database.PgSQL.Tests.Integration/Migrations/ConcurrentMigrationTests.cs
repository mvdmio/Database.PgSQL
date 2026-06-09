using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using Testcontainers.PostgreSql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

/// <summary>
///    Tests that the migration runner serializes concurrent runs with its session-scoped advisory lock.
///    These tests use their own PostgreSQL container and independent connections (not the rolled-back
///    <see cref="Fixture.TestBase" />, which cannot observe cross-session contention).
/// </summary>
public class ConcurrentMigrationTests : IAsyncLifetime
{
   private const int CONCURRENT_INSTANCES = 3;

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
   public async Task MigrateDatabaseToLatestAsync_WithConcurrentInstances_AppliesEachMigrationExactlyOnce()
   {
      // A migration set that includes a deliberately slow migration to widen the contention window:
      // a broken implementation that does not serialize would have all instances racing through this gap.
      var connectionString = _dbContainer.GetConnectionString();

      var startSignal = new TaskCompletionSource();

      var runs = Enumerable.Range(0, CONCURRENT_INSTANCES).Select(_ => Task.Run(async () =>
      {
         await using var db = _connectionFactory.BuildConnection(connectionString);
         var migrator = new DatabaseMigrator(db, new SlowMigrationSet());

         await startSignal.Task;
         await migrator.MigrateDatabaseToLatestAsync(CancellationToken);
      })).ToArray();

      // Release all instances at once to maximise the chance of hitting the race.
      startSignal.SetResult();

      // (a) No instance throws.
      var act = async () => await Task.WhenAll(runs);
      await act.Should().NotThrowAsync();

      await using var verifyDb = _connectionFactory.BuildConnection(connectionString);

      // (b) Each migration identifier appears exactly once in mvdmio.migrations.
      var executedIdentifiers = (await verifyDb.Dapper.QueryAsync<long>(
         """SELECT identifier FROM "mvdmio"."migrations" ORDER BY identifier""",
         ct: CancellationToken
      )).ToArray();

      executedIdentifiers.Should().Equal(SlowMigrationSet.Identifiers);

      // (c) The final schema is correct: every table created by the migration set exists.
      (await verifyDb.Management.TableExistsAsync("public", "concurrent_table_one")).Should().BeTrue();
      (await verifyDb.Management.TableExistsAsync("public", "concurrent_table_slow")).Should().BeTrue();
      (await verifyDb.Management.TableExistsAsync("public", "concurrent_table_three")).Should().BeTrue();
   }

   /// <summary>
   ///    A fixed migration set whose middle migration sleeps to widen the concurrency window.
   /// </summary>
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

   // These helpers set Identifier/Name explicitly, so the class-name-format analyzer does not apply.
#pragma warning disable PGSQL0001
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
         // pg_sleep widens the window during which a broken (non-serialized) implementation would let
         // other instances race past the empty-database check and the migration loop.
         await db.Dapper.ExecuteAsync("SELECT pg_sleep(1)");
         await db.Dapper.ExecuteAsync($"CREATE TABLE public.{tableName} (id BIGINT NOT NULL PRIMARY KEY)");
      }
   }
#pragma warning restore PGSQL0001
}
