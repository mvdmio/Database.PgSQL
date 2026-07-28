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

      // Both text columns permit null while the definition over them claims otherwise, which is what makes the failure
      // mode of an unverified nullability claim observable.
      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_unverified_claims (
            claim_id BIGINT PRIMARY KEY,
            label    TEXT NULL,
            note     TEXT NULL
         )
         """
      );

      // The composite-key table set, separate from the author-and-book one so that set keeps pinning the single-column
      // key path. Real composite foreign keys, and a matching index on each side of the relation the join tests read —
      // except for primary_task_id, which carries none because tasks already reference projects and a constraint the
      // other way could not be satisfied by either insertion order.
      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_tenant_projects (
            account_id      BIGINT NOT NULL,
            project_id      BIGINT GENERATED ALWAYS AS IDENTITY,
            code            TEXT NOT NULL UNIQUE,
            name            TEXT NOT NULL,
            primary_task_id BIGINT NULL,
            PRIMARY KEY (account_id, project_id)
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_tenant_tasks (
            account_id BIGINT NOT NULL,
            task_id    BIGINT NOT NULL,
            project_id BIGINT NOT NULL,
            title      TEXT NOT NULL,
            PRIMARY KEY (account_id, task_id),
            FOREIGN KEY (account_id, project_id) REFERENCES public.generated_tenant_projects (account_id, project_id)
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE INDEX IF NOT EXISTS generated_tenant_tasks_project_idx
         ON public.generated_tenant_tasks (account_id, project_id)
         """
      );

      // project_ref is stored and generated, non-null only for its own kind. A composite foreign key over it is valid
      // even so: under MATCH SIMPLE a row with a null in the key satisfies the constraint.
      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_tenant_links (
            account_id  BIGINT NOT NULL,
            link_id     BIGINT NOT NULL,
            kind        TEXT NOT NULL,
            ordinal     INTEGER NOT NULL,
            target_id   BIGINT NOT NULL,
            project_ref BIGINT GENERATED ALWAYS AS (CASE WHEN kind = 'project' THEN target_id END) STORED,
            PRIMARY KEY (account_id, link_id, kind, ordinal),
            FOREIGN KEY (account_id, project_ref) REFERENCES public.generated_tenant_projects (account_id, project_id)
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
