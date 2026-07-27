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

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_authors (
            author_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            name      TEXT NOT NULL UNIQUE,
            mentor_id BIGINT NULL REFERENCES public.generated_authors (author_id)
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_books (
            book_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            title     TEXT NOT NULL UNIQUE,
            author_id BIGINT NULL REFERENCES public.generated_authors (author_id),
            editor_id BIGINT NULL REFERENCES public.generated_authors (author_id)
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_tags (
            tag_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            label  TEXT NOT NULL UNIQUE
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_book_tags (
            book_tag_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            book_id     BIGINT NOT NULL REFERENCES public.generated_books (book_id),
            tag_id      BIGINT NOT NULL REFERENCES public.generated_tags (tag_id)
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
