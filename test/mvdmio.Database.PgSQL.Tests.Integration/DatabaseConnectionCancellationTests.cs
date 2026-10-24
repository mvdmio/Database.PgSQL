using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration;

/// <summary>
/// Tests that a cancelled Dapper call throws OperationCanceledException and not QueryException, and leaves the connection usable.
/// </summary>
public class DatabaseConnectionCancellationTests : TestBase
{
   private readonly TestFixture _fixture;

   public DatabaseConnectionCancellationTests(TestFixture fixture)
      : base(fixture)
   {
      _fixture = fixture;
   }

   [Fact]
   public async Task ExecuteAsync_WhenTokenAlreadyCancelled_ThrowsOperationCanceledException()
   {
      // Arrange
      await using var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      // Act
      var thrown = await Record.ExceptionAsync(() => connection.Dapper.ExecuteAsync("SELECT 1", ct: cts.Token));

      // Assert
      thrown.Should().BeAssignableTo<OperationCanceledException>();
      thrown.Should().NotBeOfType<QueryException>();
   }

   [Fact]
   public async Task ExecuteScalarAsync_WhenTokenAlreadyCancelled_ThrowsOperationCanceledException()
   {
      // Arrange
      await using var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      // Act
      var thrown = await Record.ExceptionAsync(() => connection.Dapper.ExecuteScalarAsync<int>("SELECT 1", ct: cts.Token));

      // Assert
      thrown.Should().BeAssignableTo<OperationCanceledException>();
      thrown.Should().NotBeOfType<QueryException>();
   }

   [Fact]
   public async Task ExecuteAsync_WhenCancelledWhileRunning_ThrowsOperationCanceledExceptionAndConnectionStaysUsable()
   {
      // Arrange
      await using var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
      cts.CancelAfter(TimeSpan.FromMilliseconds(200));

      // Act
      var thrown = await Record.ExceptionAsync(() => connection.Dapper.ExecuteAsync("SELECT pg_sleep(30)", ct: cts.Token));

      // Assert
      thrown.Should().BeAssignableTo<OperationCanceledException>();
      thrown.Should().NotBeOfType<QueryException>();

      var result = await connection.Dapper.ExecuteScalarAsync<int>("SELECT 1", ct: CancellationToken);
      result.Should().Be(1);
   }

   [Fact]
   public async Task ExecuteAsync_WhenSqlIsInvalid_ThrowsQueryExceptionWithSql()
   {
      // Arrange
      const string SQL = "SELECT * FROM table_that_does_not_exist";
      await using var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

      // Act
      var thrown = await Record.ExceptionAsync(() => connection.Dapper.ExecuteAsync(SQL, ct: CancellationToken));

      // Assert
      thrown.Should().BeOfType<QueryException>().Which.Sql.Should().Be(SQL);
   }
}
