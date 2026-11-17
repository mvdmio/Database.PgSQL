using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using Npgsql;
using Testcontainers.PostgreSql;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

/// <summary>
///    Tests that a migration can run outside a transaction (<see cref="IDbMigration.RunsInTransaction" /> is
///    <c>false</c>). These tests use their own PostgreSQL container and independent connections (not the
///    rolled-back <see cref="Fixture.TestBase" />, whose transaction would hide the behaviour under test).
/// </summary>
public class NonTransactionalMigrationTests : IAsyncLifetime
{
   private const long CREATE_TABLE_IDENTIFIER = 202601010001;
   private const long MIGRATION_UNDER_TEST_IDENTIFIER = 202601010002;
   private const string CONCURRENT_INDEX_ERROR = "CREATE INDEX CONCURRENTLY cannot run inside a transaction block";

   private PostgreSqlContainer _dbContainer = null!;
   private DatabaseConnectionFactory _connectionFactory = null!;

   private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

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
   public async Task MigrateDatabaseToLatestAsync_WithTransactionalMigrationCreatingIndexConcurrently_ThrowsAndRecordsNoRow()
   {
      var connectionString = _dbContainer.GetConnectionString();

      await using var db = _connectionFactory.BuildConnection(connectionString);
      var migrator = BuildMigrator(db, CreateTableMigration("idx_table"), CreateIndexConcurrentlyMigration(runsInTransaction: true));

      var act = async () => await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var failure = (await act.Should().ThrowAsync<MigrationException>()).Which;
      failure.Migration.Identifier.Should().Be(MIGRATION_UNDER_TEST_IDENTIFIER);
      MessagesOf(failure).Should().Contain(CONCURRENT_INDEX_ERROR);

      (await ExecutedIdentifiersAsync(db)).Should().Equal(CREATE_TABLE_IDENTIFIER);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithNonTransactionalMigrationCreatingIndexConcurrently_BuildsValidIndexAndRecordsOneRow()
   {
      var connectionString = _dbContainer.GetConnectionString();

      await using var db = _connectionFactory.BuildConnection(connectionString);
      var migrator = BuildMigrator(db, CreateTableMigration("idx_table"), CreateIndexConcurrentlyMigration(runsInTransaction: false));

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var indexIsValid = await db.Dapper.ExecuteScalarAsync<bool>(
         "SELECT i.indisvalid FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid WHERE c.relname = 'idx_table_value'",
         ct: CancellationToken
      );

      indexIsValid.Should().BeTrue();
      (await ExecutedIdentifiersAsync(db)).Should().Equal(CREATE_TABLE_IDENTIFIER, MIGRATION_UNDER_TEST_IDENTIFIER);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithNonTransactionalMigrationFailingPartway_KeepsEarlierStatementsRecordsNoRowAndRerunsFromTheStart()
   {
      var connectionString = _dbContainer.GetConnectionString();

      await using var db = _connectionFactory.BuildConnection(connectionString);
      await db.Dapper.ExecuteAsync("CREATE TABLE public.a (id INT NOT NULL)", ct: CancellationToken);

      var migration = new TestMigration(
         MIGRATION_UNDER_TEST_IDENTIFIER,
         "InsertIntoAThenB",
         runsInTransaction: false,
         async connection =>
         {
            await connection.Dapper.ExecuteAsync("INSERT INTO public.a (id) VALUES (1)");
            await connection.Dapper.ExecuteAsync("INSERT INTO public.b (id) VALUES (1)");
         }
      );

      var migrator = BuildMigrator(db, migration);

      var act = async () => await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      var failure = (await act.Should().ThrowAsync<MigrationException>()).Which;
      failure.Migration.Identifier.Should().Be(MIGRATION_UNDER_TEST_IDENTIFIER);
      MessagesOf(failure).Should().Contain("public.b").And.Contain("does not exist");

      (await db.Dapper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM public.a", ct: CancellationToken)).Should().Be(1);
      (await ExecutedIdentifiersAsync(db)).Should().BeEmpty();

      await db.Dapper.ExecuteAsync("CREATE TABLE public.b (id INT NOT NULL)", ct: CancellationToken);

      await migrator.MigrateDatabaseToLatestAsync(CancellationToken);

      (await db.Dapper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM public.a", ct: CancellationToken)).Should().Be(2);
      (await db.Dapper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM public.b", ct: CancellationToken)).Should().Be(1);
      (await ExecutedIdentifiersAsync(db)).Should().Equal(MIGRATION_UNDER_TEST_IDENTIFIER);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithNonTransactionalMigrationSleeping_DoesNotBlockInsertFromSecondConnection()
   {
      var connectionString = _dbContainer.GetConnectionString();
      var alterDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

      await using var migratorDb = _connectionFactory.BuildConnection(connectionString);
      var migrator = BuildMigrator(migratorDb, CreateTableMigration("t"), AlterThenSleepMigration(runsInTransaction: false, alterDone));
      var migrationRun = Task.Run(() => migrator.MigrateDatabaseToLatestAsync(CancellationToken), CancellationToken);

      await WaitForSignalAsync(alterDone, migrationRun);

      await using var writerDb = BuildConnectionWithStatementTimeout(connectionString, TimeSpan.FromSeconds(2));
      await writerDb.Dapper.ExecuteAsync("INSERT INTO public.t (id) VALUES (1)", ct: CancellationToken);

      await migrationRun;

      (await writerDb.Dapper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM public.t", ct: CancellationToken)).Should().Be(1);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithTransactionalMigrationSleeping_BlocksInsertFromSecondConnectionUntilStatementTimeout()
   {
      var connectionString = _dbContainer.GetConnectionString();
      var alterDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

      await using var migratorDb = _connectionFactory.BuildConnection(connectionString);
      var migrator = BuildMigrator(migratorDb, CreateTableMigration("t"), AlterThenSleepMigration(runsInTransaction: true, alterDone));
      var migrationRun = Task.Run(() => migrator.MigrateDatabaseToLatestAsync(CancellationToken), CancellationToken);

      await WaitForSignalAsync(alterDone, migrationRun);

      await using var writerDb = BuildConnectionWithStatementTimeout(connectionString, TimeSpan.FromSeconds(2));
      var act = async () => await writerDb.Dapper.ExecuteAsync("INSERT INTO public.t (id) VALUES (1)", ct: CancellationToken);

      var failure = (await act.Should().ThrowAsync<QueryException>()).Which;
      failure.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.QueryCanceled);

      await migrationRun;

      (await writerDb.Dapper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM public.t", ct: CancellationToken)).Should().Be(0);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WithConcurrentInstancesAndNonTransactionalMigration_AppliesAndRecordsItOnce()
   {
      var connectionString = _dbContainer.GetConnectionString();
      var startSignal = new TaskCompletionSource();

      var runs = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
      {
         await using var db = _connectionFactory.BuildConnection(connectionString);
         var migrator = BuildMigrator(db, CreateRowOnceMigration());

         await startSignal.Task;
         await migrator.MigrateDatabaseToLatestAsync(CancellationToken);
      }, CancellationToken)).ToArray();

      startSignal.SetResult();

      var act = async () => await Task.WhenAll(runs);
      await act.Should().NotThrowAsync();

      await using var verifyDb = _connectionFactory.BuildConnection(connectionString);

      (await verifyDb.Dapper.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM public.once_table", ct: CancellationToken)).Should().Be(1);
      (await ExecutedIdentifiersAsync(verifyDb)).Should().Equal(MIGRATION_UNDER_TEST_IDENTIFIER);
   }

   private static DatabaseMigrator BuildMigrator(DatabaseConnection db, params IDbMigration[] migrations)
   {
      return new DatabaseMigrator(db, NullLoggerFactory.Instance, new FixedMigrationSet(migrations));
   }

   private DatabaseConnection BuildConnectionWithStatementTimeout(string connectionString, TimeSpan timeout)
   {
      return _connectionFactory.BuildConnection($"{connectionString};Options=-c statement_timeout={(int)timeout.TotalMilliseconds}");
   }

   private static async Task<long[]> ExecutedIdentifiersAsync(DatabaseConnection db)
   {
      var identifiers = await db.Dapper.QueryAsync<long>(
         """SELECT identifier FROM "mvdmio"."migrations" ORDER BY identifier""",
         ct: CancellationToken
      );

      return identifiers.ToArray();
   }

   /// <summary>
   ///    Waits until the migration under test signals, or fails fast with the migrator's own error when the run
   ///    ends before it ever signals. The wait is bounded so a hung run fails the test instead of hanging it.
   /// </summary>
   private static async Task WaitForSignalAsync(TaskCompletionSource signal, Task migrationRun)
   {
      var finished = await Task.WhenAny(signal.Task, migrationRun).WaitAsync(TimeSpan.FromSeconds(60), CancellationToken);

      if (finished == migrationRun)
         await migrationRun;

      await signal.Task;
   }

   private static string MessagesOf(Exception exception)
   {
      var messages = new List<string>();

      for (Exception? current = exception; current is not null; current = current.InnerException)
         messages.Add(current.Message);

      return string.Join(Environment.NewLine, messages);
   }

   private static TestMigration CreateTableMigration(string tableName)
   {
      return new TestMigration(
         CREATE_TABLE_IDENTIFIER,
         "CreateTable",
         runsInTransaction: true,
         db => db.Dapper.ExecuteAsync($"CREATE TABLE public.{tableName} (id INT NOT NULL, value INT NULL)")
      );
   }

   private static TestMigration CreateIndexConcurrentlyMigration(bool runsInTransaction)
   {
      return new TestMigration(
         MIGRATION_UNDER_TEST_IDENTIFIER,
         "CreateIndexConcurrently",
         runsInTransaction,
         db => db.Dapper.ExecuteAsync("CREATE INDEX CONCURRENTLY idx_table_value ON public.idx_table (value)")
      );
   }

   private static TestMigration AlterThenSleepMigration(bool runsInTransaction, TaskCompletionSource alterDone)
   {
      return new TestMigration(
         MIGRATION_UNDER_TEST_IDENTIFIER,
         "AlterThenSleep",
         runsInTransaction,
         async db =>
         {
            await db.Dapper.ExecuteAsync("ALTER TABLE public.t ADD COLUMN extra INT NULL");
            alterDone.TrySetResult();
            await db.Dapper.ExecuteAsync("SELECT pg_sleep(5)");
         }
      );
   }

   private static TestMigration CreateRowOnceMigration()
   {
      return new TestMigration(
         MIGRATION_UNDER_TEST_IDENTIFIER,
         "CreateRowOnce",
         runsInTransaction: false,
         async db =>
         {
            await db.Dapper.ExecuteAsync("CREATE TABLE public.once_table (id INT NOT NULL)");
            await db.Dapper.ExecuteAsync("INSERT INTO public.once_table (id) VALUES (1)");
         }
      );
   }

   private sealed class FixedMigrationSet(IDbMigration[] migrations) : IMigrationRetriever
   {
      public IEnumerable<IDbMigration> RetrieveMigrations()
      {
         return migrations;
      }
   }

   // These helpers set Identifier/Name/Scope explicitly, so the class-name-format analyzer does not apply.
#pragma warning disable PGSQL0001
   private sealed class TestMigration(long identifier, string name, bool runsInTransaction, Func<DatabaseConnection, Task> up) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => name;
      public string Scope => "NonTransactionalMigrationTests";
      public bool RunsInTransaction => runsInTransaction;

      public Task UpAsync(DatabaseConnection db)
      {
         return up(db);
      }
   }
#pragma warning restore PGSQL0001
}
