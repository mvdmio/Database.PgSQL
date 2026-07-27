using Microsoft.Extensions.Logging.Abstractions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(TestFixture))]
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

      await using var connection = new DatabaseConnection(DbContainer.GetConnectionString());

      var databaseMigrator = new DatabaseMigrator(connection, NullLoggerFactory.Instance, GetType().Assembly);
      await databaseMigrator.MigrateDatabaseToLatestAsync();

      // Committed rather than migrated: the migration tests assert on the exact set of migrations this assembly ships.
      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_profiles (
            profile_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            handle     TEXT NOT NULL UNIQUE,
            nickname   TEXT NULL,
            birth_date DATE NOT NULL,
            wake_time  TIME NOT NULL,
            home_page  TEXT NULL,
            metadata   JSONB NULL
         )
         """
      );
   }

   public async ValueTask DisposeAsync()
   {
      await DbContainer.StopAsync();
      await DbContainer.DisposeAsync();
   }
}
