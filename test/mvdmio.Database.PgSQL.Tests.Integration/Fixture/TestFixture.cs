using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Testcontainers.PostgreSql;

[assembly:AssemblyFixture(typeof(TestFixture))]
namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

public sealed class TestFixture : IAsyncLifetime
{
   private readonly PostgreSqlContainer _dbContainer;
   
   public DatabaseConnection Db { get; private set; } = null!;

   public TestFixture()
   {
      _dbContainer = new PostgreSqlBuilder().Build();
   }
   
   public async ValueTask InitializeAsync()
   {
      await _dbContainer.StartAsync();

      Db = new DatabaseConnectionFactory().ForConnectionString(_dbContainer.GetConnectionString());
   }

   public async ValueTask DisposeAsync()
   {
      await _dbContainer.StopAsync();
      await _dbContainer.DisposeAsync();
   }
}