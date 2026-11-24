using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

public class MigrationsTableManagerTests : TestBase
{
   public MigrationsTableManagerTests(TestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public async Task EnsureTableAsync_OnFreshDatabase_CreatesTableWithScopeUniqueIndexAndNoPrimaryKey()
   {
      await DropMigrationsTableAsync();

      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);

      var columns = await GetColumnsAsync();
      columns.Should().HaveCount(5);
      columns.Should().Contain(c => c.ColumnName == "scope" && c.DataType == "text" && c.IsNullable == "YES");
      columns.Should().Contain(c => c.ColumnName == "phase" && c.DataType == "text" && c.IsNullable == "YES");

      (await GetPrimaryKeyCountAsync()).Should().Be(0);
      (await GetIndexNamesAsync()).Should().Contain(MigrationsTableManager.UNIQUE_INDEX_NAME);
      (await GetUniqueIndexDefinitionAsync()).Should().Contain("(scope, identifier, phase)");
   }

   [Fact]
   public async Task EnsureTableAsync_OnLegacyTable_DropsPrimaryKeyAndAddsScopeAndUniqueIndex()
   {
      await DropMigrationsTableAsync();
      await CreateLegacyTableAsync();

      await Db.Dapper.ExecuteAsync(
         """INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at) VALUES (202505181000, 'Legacy', NOW())""",
         ct: CancellationToken);

      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);

      var columns = await GetColumnsAsync();
      columns.Should().Contain(c => c.ColumnName == "scope" && c.DataType == "text" && c.IsNullable == "YES");

      (await GetPrimaryKeyCountAsync()).Should().Be(0);
      (await GetIndexNamesAsync()).Should().Contain(MigrationsTableManager.UNIQUE_INDEX_NAME);

      var scopes = (await Db.Dapper.QueryAsync<string?>(
         """SELECT scope FROM "mvdmio"."migrations" """,
         ct: CancellationToken)).ToArray();
      scopes.Should().ContainSingle().Which.Should().BeNull();
   }

   [Fact]
   public async Task EnsureTableAsync_RunTwice_IsIdempotent()
   {
      await DropMigrationsTableAsync();

      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);
      var firstShape = await GetShapeAsync();

      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);
      var secondShape = await GetShapeAsync();

      secondShape.Should().Be(firstShape);
   }

   [Fact]
   public async Task EnsureTableAsync_FreshAndUpgradedTables_HaveTheSameShape()
   {
      await DropMigrationsTableAsync();
      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);
      var freshShape = await GetShapeAsync();

      await DropMigrationsTableAsync();
      await CreateLegacyTableAsync();
      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);
      var upgradedShape = await GetShapeAsync();

      upgradedShape.Should().Be(freshShape);
   }

   [Fact]
   public async Task EnsureTableAsync_AllowsSameIdentifierInDifferentScopes_ButNotWithinOneScope()
   {
      await DropMigrationsTableAsync();
      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);

      await Db.Dapper.ExecuteAsync(
         """
         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at, scope, phase)
         VALUES (202505181000, 'A', NOW(), 'ScopeA', 'pre-deploy'), (202505181000, 'B', NOW(), 'ScopeB', 'pre-deploy')
         """,
         ct: CancellationToken);

      var duplicateInsert = async () => await Db.Dapper.ExecuteAsync(
         """
         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at, scope, phase)
         VALUES (202505181000, 'A again', NOW(), 'ScopeA', 'pre-deploy')
         """,
         ct: CancellationToken);

      await duplicateInsert.Should().ThrowAsync<Exception>("the named unique index must reject a duplicate (scope, identifier, phase) triple");
   }

   [Fact]
   public async Task EnsureTableAsync_OnVersion045Table_AddsPhaseKeepsRowsAndIndexNameAndCoversPhase()
   {
      await DropMigrationsTableAsync();
      await CreateVersion045TableAsync();

      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);

      var columns = await GetColumnsAsync();
      columns.Should().Contain(c => c.ColumnName == "phase" && c.DataType == "text" && c.IsNullable == "YES");

      var rows = (await Db.Dapper.QueryAsync<HistoryRow>(
         """SELECT identifier, name, scope, phase FROM "mvdmio"."migrations" ORDER BY identifier""",
         ct: CancellationToken)).ToArray();
      rows.Should().Equal(new HistoryRow(202505181000, "First", "ScopeA", null), new HistoryRow(202505181001, "Second", null, null));

      (await GetIndexNamesAsync()).Should().Equal(MigrationsTableManager.UNIQUE_INDEX_NAME);
      (await GetUniqueIndexDefinitionAsync()).Should().Contain("(scope, identifier, phase)");
   }

   [Fact]
   public async Task EnsureTableAsync_OnVersion045Table_ChangesNothingOnASecondCall()
   {
      await DropMigrationsTableAsync();
      await CreateVersion045TableAsync();

      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);
      var firstShape = await GetShapeAsync();
      var firstIndexOid = await GetUniqueIndexOidAsync();

      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);

      (await GetShapeAsync()).Should().Be(firstShape);
      (await GetUniqueIndexOidAsync()).Should().Be(firstIndexOid, "a table already in the current shape must not be touched");
   }

   [Fact]
   public async Task EnsureTableAsync_OnVersion045Table_LeavesTheVersion045ShapeCheckTrue()
   {
      await DropMigrationsTableAsync();
      await CreateVersion045TableAsync();

      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);

      var version045ShapeCheck = await Db.Dapper.ExecuteScalarAsync<bool>(
         """
         SELECT EXISTS (
                   SELECT 1 FROM information_schema.columns
                   WHERE table_schema = 'mvdmio' AND table_name = 'migrations' AND column_name = 'scope'
                )
            AND EXISTS (
                   SELECT 1 FROM pg_indexes
                   WHERE schemaname = 'mvdmio' AND tablename = 'migrations' AND indexname = 'migrations_scope_identifier_key'
                )
            AND NOT EXISTS (
                   SELECT 1
                   FROM pg_constraint c
                   JOIN pg_class t ON c.conrelid = t.oid
                   JOIN pg_namespace n ON t.relnamespace = n.oid
                   WHERE n.nspname = 'mvdmio' AND t.relname = 'migrations' AND c.contype = 'p'
                )
         """,
         ct: CancellationToken);

      version045ShapeCheck.Should().BeTrue();
   }

   [Fact]
   public async Task EnsureTableAsync_WithPhaseColumnButIndexNotCoveringPhase_RebuildsTheIndexOnPhase()
   {
      await DropMigrationsTableAsync();
      await CreateVersion045TableAsync();
      await Db.Dapper.ExecuteAsync("""ALTER TABLE "mvdmio"."migrations" ADD COLUMN phase TEXT NULL""", ct: CancellationToken);

      await MigrationsTableManager.EnsureTableAsync(Db, CancellationToken);

      (await GetUniqueIndexDefinitionAsync()).Should().Contain("(scope, identifier, phase)");
   }

   private async Task CreateVersion045TableAsync()
   {
      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            scope       TEXT        NULL
         );

         CREATE UNIQUE INDEX migrations_scope_identifier_key ON "mvdmio"."migrations" (scope, identifier);

         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at, scope)
         VALUES (202505181000, 'First', NOW(), 'ScopeA'), (202505181001, 'Second', NOW(), NULL);
         """,
         ct: CancellationToken);
   }

   private async Task<string> GetUniqueIndexDefinitionAsync()
   {
      return (await Db.Dapper.QueryAsync<string>(
         $"SELECT indexdef FROM pg_indexes WHERE schemaname = 'mvdmio' AND tablename = 'migrations' AND indexname = '{MigrationsTableManager.UNIQUE_INDEX_NAME}'",
         ct: CancellationToken)).Single();
   }

   private async Task<long> GetUniqueIndexOidAsync()
   {
      return await Db.Dapper.ExecuteScalarAsync<long>(
         $"SELECT c.oid::bigint FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'mvdmio' AND c.relname = '{MigrationsTableManager.UNIQUE_INDEX_NAME}'",
         ct: CancellationToken);
   }

   private async Task DropMigrationsTableAsync()
   {
      await Db.Dapper.ExecuteAsync("""DROP TABLE IF EXISTS "mvdmio"."migrations";""", ct: CancellationToken);
   }

   private async Task CreateLegacyTableAsync()
   {
      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE "mvdmio"."migrations" (
            identifier  BIGINT      NOT NULL,
            name        TEXT        NOT NULL,
            executed_at TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (identifier)
         );
         """,
         ct: CancellationToken);
   }

   private async Task<ColumnRow[]> GetColumnsAsync()
   {
      return (await Db.Dapper.QueryAsync<ColumnRow>(
         """
         SELECT
            column_name AS columnName,
            data_type AS dataType,
            is_nullable AS isNullable,
            ordinal_position AS ordinalPosition
         FROM information_schema.columns
         WHERE table_schema = 'mvdmio' AND table_name = 'migrations'
         ORDER BY ordinal_position
         """,
         ct: CancellationToken)).ToArray();
   }

   private async Task<long> GetPrimaryKeyCountAsync()
   {
      return await Db.Dapper.ExecuteScalarAsync<long>(
         """
         SELECT COUNT(*)
         FROM pg_constraint c
         JOIN pg_class t ON c.conrelid = t.oid
         JOIN pg_namespace n ON t.relnamespace = n.oid
         WHERE n.nspname = 'mvdmio' AND t.relname = 'migrations' AND c.contype = 'p'
         """,
         ct: CancellationToken);
   }

   private async Task<string[]> GetIndexNamesAsync()
   {
      return (await Db.Dapper.QueryAsync<string>(
         "SELECT indexname FROM pg_indexes WHERE schemaname = 'mvdmio' AND tablename = 'migrations' ORDER BY indexname",
         ct: CancellationToken)).ToArray();
   }

   private async Task<string> GetShapeAsync()
   {
      var columns = await GetColumnsAsync();
      var pkCount = await GetPrimaryKeyCountAsync();
      var indexDefinitions = (await Db.Dapper.QueryAsync<string>(
         "SELECT indexdef FROM pg_indexes WHERE schemaname = 'mvdmio' AND tablename = 'migrations' ORDER BY indexname",
         ct: CancellationToken)).ToArray();

      var columnPart = string.Join("|", columns.Select(c => $"{c.OrdinalPosition}:{c.ColumnName}:{c.DataType}:{c.IsNullable}"));
      return $"{columnPart};pk={pkCount};{string.Join("|", indexDefinitions)}";
   }

   private sealed record HistoryRow(long Identifier, string Name, string? Scope, string? Phase);

   private sealed record ColumnRow(string ColumnName, string DataType, string IsNullable, int OrdinalPosition);
}
