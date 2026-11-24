namespace mvdmio.Database.PgSQL.Migrations;

internal static class MigrationsTableManager
{
   public const string UNIQUE_INDEX_NAME = "migrations_scope_identifier_key";

   public const string PRE_DEPLOY_PHASE_TEXT = "pre-deploy";
   public const string POST_DEPLOY_PHASE_TEXT = "post-deploy";

   public static async Task EnsureTableAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
   {
      if (await IsTableInCurrentShapeAsync(connection, cancellationToken))
         return;

      await connection.Dapper.ExecuteAsync(
         $"""
         CREATE SCHEMA IF NOT EXISTS "mvdmio";

         CREATE TABLE IF NOT EXISTS "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            scope       TEXT        NULL,
            phase       TEXT        NULL
         );

         ALTER TABLE "mvdmio"."migrations" ADD COLUMN IF NOT EXISTS scope TEXT NULL;
         ALTER TABLE "mvdmio"."migrations" ADD COLUMN IF NOT EXISTS phase TEXT NULL;
         ALTER TABLE "mvdmio"."migrations" DROP CONSTRAINT IF EXISTS migrations_pkey;

         DROP INDEX IF EXISTS "mvdmio".{UNIQUE_INDEX_NAME};
         CREATE UNIQUE INDEX {UNIQUE_INDEX_NAME} ON "mvdmio"."migrations" (scope, identifier, phase);
         """,
         ct: cancellationToken
      );
   }

   public static async Task<bool> ScopeColumnExistsAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
   {
      return await ColumnExistsAsync(connection, "scope", cancellationToken);
   }

   public static async Task<bool> PhaseColumnExistsAsync(DatabaseConnection connection, CancellationToken cancellationToken = default)
   {
      return await ColumnExistsAsync(connection, "phase", cancellationToken);
   }

   private static async Task<bool> ColumnExistsAsync(DatabaseConnection connection, string columnName, CancellationToken cancellationToken)
   {
      return await connection.Dapper.ExecuteScalarAsync<bool>(
         $"""
         SELECT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'mvdmio' AND table_name = 'migrations' AND column_name = '{columnName}'
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
                   SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'mvdmio' AND table_name = 'migrations' AND column_name = 'phase'
                )
            AND EXISTS (
                   SELECT 1 FROM pg_indexes
                   WHERE schemaname = 'mvdmio' AND tablename = 'migrations' AND indexname = '{UNIQUE_INDEX_NAME}'
                     AND indexdef LIKE '%(scope, identifier, phase)%'
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
