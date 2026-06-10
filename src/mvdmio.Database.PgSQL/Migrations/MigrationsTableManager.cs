namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Creates and upgrades the <c>mvdmio.migrations</c> tracking table. A freshly created table and a legacy
///    table upgraded in place end up with the same shape: a nullable <c>scope</c> column, no primary key, and
///    a named <c>UNIQUE (scope, identifier)</c> index. The caller must hold the migration advisory lock so
///    concurrent instances upgrade the table exactly once.
/// </summary>
internal static class MigrationsTableManager
{
   /// <summary>
   ///    Name of the unique index on (scope, identifier). Named so the next major version can promote it to
   ///    the primary key via <c>ADD PRIMARY KEY USING INDEX</c> once <c>scope</c> becomes <c>NOT NULL</c>.
   /// </summary>
   public const string UNIQUE_INDEX_NAME = "migrations_scope_identifier_key";

   /// <summary>
   ///    Idempotently creates the migrations table in its current shape, or upgrades a legacy table in place:
   ///    adds the nullable <c>scope</c> column, drops the legacy <c>PRIMARY KEY (identifier)</c>, and adds the
   ///    named <c>UNIQUE (scope, identifier)</c> index. Re-running against an up-to-date table is a no-op.
   /// </summary>
   /// <param name="connection">The database connection to use.</param>
   /// <param name="cancellationToken">A cancellation token.</param>
   public static async Task EnsureTableAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
   {
      // Read-only probe first: steady-state runs (table already in its current shape) must issue no DDL at
      // all. ALTER TABLE takes an ACCESS EXCLUSIVE lock and requires table ownership even when its
      // IF [NOT] EXISTS subcommands are no-ops, so unconditionally running the batch would contend with
      // other sessions and break restricted roles that can use the table but do not own it.
      if (await IsTableInCurrentShapeAsync(connection, cancellationToken))
         return;

      // scope is last in the fresh CREATE so a fresh table and a legacy table upgraded via ADD COLUMN
      // end up with identical column order (and therefore identical exported schemas).
      // scope stays nullable this major version: legacy rows are attributed by the backfill, and the next
      // major version makes it NOT NULL and promotes the unique index to PRIMARY KEY (scope, identifier).
      await connection.Dapper.ExecuteAsync(
         $"""
         CREATE SCHEMA IF NOT EXISTS "mvdmio";

         CREATE TABLE IF NOT EXISTS "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            scope       TEXT        NULL
         );

         ALTER TABLE "mvdmio"."migrations" ADD COLUMN IF NOT EXISTS scope TEXT NULL;
         ALTER TABLE "mvdmio"."migrations" DROP CONSTRAINT IF EXISTS migrations_pkey;

         CREATE UNIQUE INDEX IF NOT EXISTS {UNIQUE_INDEX_NAME} ON "mvdmio"."migrations" (scope, identifier);
         """,
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Checks whether the migrations table has the <c>scope</c> column, i.e. whether
   ///    <see cref="EnsureTableAsync" /> has upgraded (or created) it on this database yet.
   /// </summary>
   /// <param name="connection">The database connection to use.</param>
   /// <param name="cancellationToken">A cancellation token.</param>
   public static async Task<bool> ScopeColumnExistsAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
   {
      return await connection.Dapper.ExecuteScalarAsync<bool>(
         """
         SELECT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'mvdmio' AND table_name = 'migrations' AND column_name = 'scope'
         )
         """,
         ct: cancellationToken
      );
   }

   private static async Task<bool> IsTableInCurrentShapeAsync(DatabaseConnection connection, CancellationToken cancellationToken)
   {
      return await connection.Dapper.ExecuteScalarAsync<bool>(
         $"""
         SELECT EXISTS (
                   SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'mvdmio' AND table_name = 'migrations' AND column_name = 'scope'
                )
            AND EXISTS (
                   SELECT 1 FROM pg_indexes
                   WHERE schemaname = 'mvdmio' AND tablename = 'migrations' AND indexname = '{UNIQUE_INDEX_NAME}'
                )
            AND NOT EXISTS (
                   SELECT 1
                   FROM pg_constraint c
                   JOIN pg_class t ON c.conrelid = t.oid
                   JOIN pg_namespace n ON t.relnamespace = n.oid
                   WHERE n.nspname = 'mvdmio' AND t.relname = 'migrations' AND c.contype = 'p'
                )
         """,
         ct: cancellationToken
      );
   }
}
