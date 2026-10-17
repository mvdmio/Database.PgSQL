using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Npgsql;

namespace mvdmio.Database.PgSQL.Tests.Integration;

/// <summary>
/// Tests that a failed action inside InTransaction* surfaces its own exception, even when the backend died and the rollback cannot run.
/// </summary>
public class DatabaseConnectionTransactionFailureTests : TestBase
{
   private const int MAX_ATTEMPTS = 100;

   private readonly TestFixture _fixture;

   public DatabaseConnectionTransactionFailureTests(TestFixture fixture)
      : base(fixture)
   {
      _fixture = fixture;
   }

   private string UnpooledConnectionString()
   {
      return new NpgsqlConnectionStringBuilder(_fixture.DbContainer.GetConnectionString()) { Pooling = false }.ConnectionString;
   }

   // Terminating a backend is asynchronous, so retry until the victim notices. Returns the exception the victim saw.
   private async Task<Exception> KillBackendAsync(DatabaseConnection victim, DatabaseConnection killer)
   {
      var pid = await victim.Dapper.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", ct: CancellationToken);
      await killer.Dapper.ExecuteScalarAsync<bool>("SELECT pg_terminate_backend(@pid)", new Dictionary<string, object?> { ["pid"] = pid }, ct: CancellationToken);

      for (var attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
      {
         try
         {
            await victim.Dapper.ExecuteScalarAsync<int>("SELECT 1", ct: CancellationToken);
         }
         catch (Exception exception)
         {
            return exception;
         }

         await Task.Delay(50, CancellationToken);
      }

      throw new InvalidOperationException("The backend was not terminated.");
   }

   private Exception KillBackend(DatabaseConnection victim, DatabaseConnection killer)
   {
      return KillBackendAsync(victim, killer).GetAwaiter().GetResult();
   }

   [Fact]
   public async Task InTransactionAsync_WhenBackendDiesInAction_ThrowsTheActionsException()
   {
      // Arrange
      await using var victim = new DatabaseConnection(UnpooledConnectionString());
      await using var killer = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      Exception? seen = null;

      // Act
      var thrown = await Record.ExceptionAsync(() => victim.InTransactionAsync(async () =>
      {
         seen = await KillBackendAsync(victim, killer);
         throw seen;
      }));

      // Assert
      seen.Should().NotBeNull();
      thrown.Should().BeSameAs(seen);
   }

   [Fact]
   public void InTransaction_WhenBackendDiesInAction_ThrowsTheActionsException()
   {
      // Arrange
      using var victim = new DatabaseConnection(UnpooledConnectionString());
      using var killer = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      Exception? seen = null;

      // Act
      var thrown = Record.Exception(() => victim.InTransaction(() =>
      {
         seen = KillBackend(victim, killer);
         throw seen;
      }));

      // Assert
      seen.Should().NotBeNull();
      thrown.Should().BeSameAs(seen);
   }

   [Fact]
   public async Task InTransactionAsyncOfT_WhenBackendDiesInAction_ThrowsTheActionsException()
   {
      // Arrange
      await using var victim = new DatabaseConnection(UnpooledConnectionString());
      await using var killer = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      Exception? seen = null;

      // Act
      var thrown = await Record.ExceptionAsync(() => victim.InTransactionAsync<int>(async () =>
      {
         seen = await KillBackendAsync(victim, killer);
         throw seen;
      }));

      // Assert
      seen.Should().NotBeNull();
      thrown.Should().BeSameAs(seen);
   }

   [Fact]
   public async Task InTransactionAsync_AfterBackendDiedInPreviousTransaction_RunsOnFreshConnection()
   {
      // Arrange
      await using var victim = new DatabaseConnection(UnpooledConnectionString());
      await using var killer = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

      await Record.ExceptionAsync(() => victim.InTransactionAsync(async () =>
      {
         throw await KillBackendAsync(victim, killer);
      }));

      // Act
      var result = await victim.InTransactionAsync(
         async () => await victim.Dapper.ExecuteScalarAsync<int>("SELECT 1", ct: CancellationToken)
      );

      // Assert
      result.Should().Be(1);
   }

   [Fact]
   public async Task RollbackTransactionAsync_WhenBackendDied_LeavesConnectionUsable()
   {
      // Arrange
      await using var victim = new DatabaseConnection(UnpooledConnectionString());
      await using var killer = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

      await victim.BeginTransactionAsync(ct: CancellationToken);
      await KillBackendAsync(victim, killer);

      // Act - the rollback may throw because the backend is gone; only the state afterwards matters.
      await Record.ExceptionAsync(() => victim.RollbackTransactionAsync(CancellationToken));

      // Assert
      (await victim.BeginTransactionAsync(ct: CancellationToken)).Should().BeTrue();
      (await victim.Dapper.ExecuteScalarAsync<int>("SELECT 1", ct: CancellationToken)).Should().Be(1);
   }

   [Fact]
   public async Task InTransactionAsync_WhenActionFailsOnHealthyConnection_RollsBack()
   {
      // Arrange - a regular table, because a temp table belongs to one connection and the test needs two.
      var table = $"rollback_test_{Guid.NewGuid():N}";
      await using var setup = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await using var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      await setup.Dapper.ExecuteAsync($"CREATE TABLE {table} (id int)", ct: CancellationToken);

      try
      {
         // Act
         var thrown = await Record.ExceptionAsync(() => connection.InTransactionAsync(async () =>
         {
            await connection.Dapper.ExecuteAsync($"INSERT INTO {table} (id) VALUES (1)", ct: CancellationToken);
            throw new InvalidOperationException("action failed");
         }));

         // Assert
         thrown.Should().BeOfType<InvalidOperationException>();
         (await setup.Dapper.ExecuteScalarAsync<long>($"SELECT count(*) FROM {table}", ct: CancellationToken)).Should().Be(0);
      }
      finally
      {
         await setup.Dapper.ExecuteAsync($"DROP TABLE {table}", ct: CancellationToken);
      }
   }
}
