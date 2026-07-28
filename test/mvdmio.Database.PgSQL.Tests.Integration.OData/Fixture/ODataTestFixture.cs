using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ODataTestFixture))]

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    Owns this assembly's PostgreSQL container. A second container in the test run is the accepted cost of keeping
///    the OData dependency out of the main integration suite.
/// </summary>
/// <remarks>
///    The table DDL is committed here rather than expressed as a migration: this project ships no migrations and
///    asserts nothing about them.
/// </remarks>
public sealed class ODataTestFixture : IAsyncLifetime
{
   public PostgreSqlContainer DbContainer { get; }

   public ODataTestFixture()
   {
      DbContainer = new PostgreSqlBuilder("postgres:18").Build();
   }

   public async ValueTask InitializeAsync()
   {
      await DbContainer.StartAsync();

      await using var connection = new DatabaseConnection(DbContainer.GetConnectionString());

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.odata_samples (
            sample_id  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            name       TEXT NOT NULL UNIQUE,
            nickname   TEXT NULL,
            rating     INTEGER NOT NULL,
            bonus      INTEGER NULL,
            amount     NUMERIC(12, 2) NOT NULL,
            created_at TIMESTAMPTZ NOT NULL,
            is_active  BOOLEAN NOT NULL,
            category   INTEGER NOT NULL,
            public_id  UUID NOT NULL,
            tier       TEXT NOT NULL
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.odata_awkward (
            awkward_id    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            home_page     TEXT NULL,
            metadata      JSONB NULL,
            birth_date    DATE NOT NULL,
            wake_time     TIME NOT NULL,
            duration      INTERVAL NOT NULL,
            payload       BYTEA NULL,
            initial       TEXT NOT NULL,
            signed_offset SMALLINT NOT NULL,
            small_count   INTEGER NOT NULL,
            medium_count  BIGINT NOT NULL,
            large_count   NUMERIC(20, 0) NOT NULL,
            occurred_at   TIMESTAMP NOT NULL
         )
         """
      );

      // The relation-bearing pair the expansion tests query through. Real foreign-key constraints, even though a
      // relation neither emits nor verifies one, so the fixture does not model something PostgreSQL would reject.
      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.odata_authors (
            author_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            name      TEXT NOT NULL UNIQUE,
            mentor_id BIGINT NULL REFERENCES public.odata_authors (author_id)
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.odata_books (
            book_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            title     TEXT NOT NULL UNIQUE,
            author_id BIGINT NULL REFERENCES public.odata_authors (author_id)
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
