using Microsoft.Extensions.DependencyInjection;
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
      DbContainer = new PostgreSqlBuilder("postgres:19").Build();
   }

   public async ValueTask InitializeAsync()
   {
      await DbContainer.StartAsync();

      new ServiceCollection().AddEnumDapperTypeHandlers([typeof(TestFixture).Assembly]);

      await using var connection = new DatabaseConnection(DbContainer.GetConnectionString());

      var databaseMigrator = new DatabaseMigrator(connection, NullLoggerFactory.Instance, GetType().Assembly);
      await databaseMigrator.MigrateDatabaseToLatestAsync();

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

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_link_people (
            person_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            name      TEXT NOT NULL
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_link_assets (
            asset_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            name     TEXT NOT NULL
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_polymorphic_links (
            link_id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            kind      TEXT NOT NULL,
            target_id BIGINT NOT NULL
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_unverified_claims (
            claim_id BIGINT PRIMARY KEY,
            label    TEXT NULL,
            note     TEXT NULL
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_storage_claims (
            claim_id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            created_at       TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
            state            TEXT NOT NULL,
            phase            TEXT NOT NULL,
            priority         INTEGER NOT NULL,
            severity         SMALLINT NOT NULL,
            epoch            BIGINT NOT NULL,
            review_state     TEXT NULL,
            review_priority  INTEGER NULL,
            document         JSONB NOT NULL,
            draft            JSON NULL,
            legacy_document  TEXT NOT NULL,
            plain_note       TEXT NOT NULL,
            offset_hours     SMALLINT NOT NULL,
            offset_claimed   SMALLINT NOT NULL,
            optional_offset  SMALLINT NULL,
            metadata         JSONB NULL,
            claimed_metadata JSONB NULL,
            json_metadata    JSON NULL
         )
         """
      );

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
      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_tenancy_documents (
            account_id  BIGINT NOT NULL,
            document_id BIGINT GENERATED ALWAYS AS IDENTITY,
            code        TEXT NOT NULL UNIQUE,
            title       TEXT NOT NULL,
            body        TEXT NOT NULL,
            PRIMARY KEY (account_id, document_id)
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_tenancy_settings (
            setting_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            account_id BIGINT NOT NULL,
            code       TEXT NOT NULL UNIQUE,
            label      TEXT NOT NULL,
            value      TEXT NOT NULL
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_tenancy_profiles (
            account_id   BIGINT NOT NULL PRIMARY KEY,
            is_active    BOOLEAN NOT NULL,
            display_name TEXT NOT NULL
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_catalog_entries (
            entry_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            sku      TEXT NULL UNIQUE
         )
         """
      );

      await connection.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_catalog_items (
            item_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            sku     TEXT NOT NULL
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
