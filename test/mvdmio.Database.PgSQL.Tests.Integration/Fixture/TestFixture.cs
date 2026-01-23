using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Testcontainers.PostgreSql;

[assembly:AssemblyFixture(typeof(TestFixture))]
namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

public sealed class TestFixture : IAsyncLifetime
{
   public PostgreSqlContainer DbContainer { get; }

   public TestFixture()
   {
      DbContainer = new PostgreSqlBuilder("postgres:18").Build();
   }

   public async ValueTask InitializeAsync()
   {
      await DbContainer.StartAsync();

      var databaseMigrator = new DatabaseMigrator(new DatabaseConnection(DbContainer.GetConnectionString()), GetType().Assembly);
      await databaseMigrator.MigrateDatabaseToLatestAsync();
   }

   public async ValueTask DisposeAsync()
   {
      await DbContainer.StopAsync();
      await DbContainer.DisposeAsync();
   }
}
