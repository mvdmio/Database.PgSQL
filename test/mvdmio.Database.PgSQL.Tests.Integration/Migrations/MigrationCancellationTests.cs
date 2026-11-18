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

public class MigrationCancellationTests : IAsyncLifetime
{
   private const long FIRST_IDENTIFIER = 202601010001;
   private const long SECOND_IDENTIFIER = 202601010002;
   private const long MIGRATION_ADVISORY_LOCK_KEY = 0x6D76_646D_696F_0001;

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

   [Theory]
   [InlineData(true)]
   [InlineData(false)]
   public async Task MigrateDatabaseToLatestAsync_WhenFirstMigrationCancelsTheToken_FinishesFirstMigrationAndStartsNoSecond(bool firstRunsInTransaction)
   {
      using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      var secondRan = false;

      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(
         db,
         new TestMigration(FIRST_IDENTIFIER, firstRunsInTransaction, _ => CancelAsync(cancellation)),
         new TestMigration(SECOND_IDENTIFIER, runsInTransaction: true, _ => MarkAsync(() => secondRan = true))
      );

      var act = async () => await migrator.MigrateDatabaseToLatestAsync(cancellation.Token);

      (await act.Should().ThrowAsync<OperationCanceledException>()).Which.Should().NotBeOfType<MigrationException>();
      secondRan.Should().BeFalse();
      (await ExecutedIdentifiersAsync(db)).Should().Equal(FIRST_IDENTIFIER);
   }

   [Fact]
   public async Task MigrateDatabaseToAsync_WhenFirstMigrationCancelsTheToken_FinishesFirstMigrationAndStartsNoSecond()
   {
      using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      var secondRan = false;

      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(
         db,
         new TestMigration(FIRST_IDENTIFIER, runsInTransaction: true, _ => CancelAsync(cancellation)),
         new TestMigration(SECOND_IDENTIFIER, runsInTransaction: true, _ => MarkAsync(() => secondRan = true))
      );

      var act = async () => await migrator.MigrateDatabaseToAsync(SECOND_IDENTIFIER, cancellation.Token);

      (await act.Should().ThrowAsync<OperationCanceledException>()).Which.Should().NotBeOfType<MigrationException>();
      secondRan.Should().BeFalse();
      (await ExecutedIdentifiersAsync(db)).Should().Equal(FIRST_IDENTIFIER);
   }

   [Fact]
   public async Task RunAsync_WithTokenCancelledBeforeStart_ThrowsOperationCanceledExceptionAndRunsNothing()
   {
      using var cancellation = new CancellationTokenSource();
      await cancellation.CancelAsync();
      var ran = false;

      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = BuildMigrator(db);

      var act = async () => await migrator.RunAsync(new TestMigration(FIRST_IDENTIFIER, runsInTransaction: true, _ => MarkAsync(() => ran = true)), cancellation.Token);

      (await act.Should().ThrowAsync<OperationCanceledException>()).Which.Should().NotBeOfType<MigrationException>();
      ran.Should().BeFalse();
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WhenCancelledWhileEmbeddedSchemaIsApplying_FinishesApplyingTheSchemaAndRunsNoMigration()
   {
      using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

      await using var db = _connectionFactory.BuildConnection(_dbContainer.GetConnectionString());
      var migrator = new DatabaseMigrator(
         db,
         environment: "ghostheader",
         new CancellingLoggerFactory(cancellation),
         [typeof(MigrationCancellationTests).Assembly],
         new ReflectionMigrationRetriever(typeof(TestFixture).Assembly, typeof(AssemblyMarker).Assembly));

      var act = async () => await migrator.MigrateDatabaseToLatestAsync(cancellation.Token);

      (await act.Should().ThrowAsync<OperationCanceledException>()).Which.Should().NotBeOfType<MigrationException>();

      (await db.Management.TableExistsAsync("public", "simple_table")).Should().BeTrue();
      (await db.Management.TableExistsAsync("public", "secondary_table")).Should().BeFalse();
      (await ExecutedIdentifiersAsync(db)).Should().Equal(202505181000);
   }

   [Fact]
   public async Task MigrateDatabaseToLatestAsync_WhenCancelledWhileWaitingForTheMigrationLock_ThrowsOperationCanceledExceptionAndRunsNothing()
   {
      using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      var ran = false;
      var connectionString = _dbContainer.GetConnectionString();

      await using var lockHolderDb = _connectionFactory.BuildConnection(connectionString);
      await lockHolderDb.OpenAsync(CancellationToken);
      await lockHolderDb.Dapper.ExecuteAsync(
         "SELECT pg_advisory_lock(:key)",
         new Dictionary<string, object?> { { "key", MIGRATION_ADVISORY_LOCK_KEY } },
         ct: CancellationToken
      );

      await using var db = _connectionFactory.BuildConnection(connectionString);
      var migrator = BuildMigrator(db, new TestMigration(FIRST_IDENTIFIER, runsInTransaction: true, _ => MarkAsync(() => ran = true)));
      var migrationRun = Task.Run(() => migrator.MigrateDatabaseToLatestAsync(cancellation.Token), CancellationToken);

      await WaitForLockWaiterAsync(lockHolderDb, migrationRun);
      await cancellation.CancelAsync();

      var act = async () => await migrationRun.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

      (await act.Should().ThrowAsync<OperationCanceledException>()).Which.Should().NotBeOfType<MigrationException>();
      ran.Should().BeFalse();
      (await lockHolderDb.Management.TableExistsAsync("mvdmio", "migrations")).Should().BeFalse();
   }

   private static DatabaseMigrator BuildMigrator(DatabaseConnection db, params IDbMigration[] migrations)
   {
      return new DatabaseMigrator(db, NullLoggerFactory.Instance, new FixedMigrationSet(migrations));
   }

   private static Task CancelAsync(CancellationTokenSource cancellation)
   {
      return cancellation.CancelAsync();
   }

   private static Task MarkAsync(Action mark)
   {
      mark();
      return Task.CompletedTask;
   }

   private static async Task<long[]> ExecutedIdentifiersAsync(DatabaseConnection db)
   {
      var identifiers = await db.Dapper.QueryAsync<long>(
         """SELECT identifier FROM "mvdmio"."migrations" ORDER BY identifier""",
         ct: CancellationToken
      );

      return identifiers.ToArray();
   }

   private static async Task WaitForLockWaiterAsync(DatabaseConnection observerDb, Task migrationRun)
   {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      timeout.CancelAfter(TimeSpan.FromSeconds(60));

      while (true)
      {
         if (migrationRun.IsCompleted)
            await migrationRun;

         var waiters = await observerDb.Dapper.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted",
            ct: timeout.Token
         );

         if (waiters > 0)
            return;

         await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
      }
   }

   private sealed class FixedMigrationSet(IDbMigration[] migrations) : IMigrationRetriever
   {
      public IEnumerable<IDbMigration> RetrieveMigrations()
      {
         return migrations;
      }
   }

#pragma warning disable PGSQL0001 // These helpers set Identifier/Name/Scope explicitly, so the class-name-format analyzer does not apply.
   private sealed class TestMigration(long identifier, bool runsInTransaction, Func<DatabaseConnection, Task> up) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => $"Migration{identifier}";
      public string Scope => "MigrationCancellationTests";
      public bool RunsInTransaction => runsInTransaction;

      public Task UpAsync(DatabaseConnection db)
      {
         return up(db);
      }
   }
#pragma warning restore PGSQL0001

   private sealed class CancellingLoggerFactory(CancellationTokenSource cancellation) : ILoggerFactory
   {
      public ILogger CreateLogger(string categoryName)
      {
         return new CancellingLogger(cancellation);
      }

      public void AddProvider(ILoggerProvider provider)
      {
      }

      public void Dispose()
      {
      }
   }

   private sealed class CancellingLogger(CancellationTokenSource cancellation) : ILogger
   {
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
         if (logLevel == LogLevel.Warning)
            cancellation.Cancel();
      }
   }
}
