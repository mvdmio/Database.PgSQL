using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.Migrations;

/// <summary>
///    Tests for the migrations-table manager: fresh create, in-place upgrade of a legacy table, idempotency,
///    and shape equality between fresh and upgraded tables. Each test drops and recreates the table inside
///    the rolled-back test transaction, so the shared fixture database is not affected.
/// </summary>
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
      columns.Should().HaveCount(4);
      columns.Should().Contain(c => c.ColumnName == "scope" && c.DataType == "text" && c.IsNullable == "YES");

      (await GetPrimaryKeyCountAsync()).Should().Be(0);
      (await GetIndexNamesAsync()).Should().Contain(MigrationsTableManager.UNIQUE_INDEX_NAME);
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

      // Existing rows survive the upgrade with a still-null scope.
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
         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at, scope)
         VALUES (202505181000, 'A', NOW(), 'ScopeA'), (202505181000, 'B', NOW(), 'ScopeB')
         """,
         ct: CancellationToken);

      var duplicateInsert = async () => await Db.Dapper.ExecuteAsync(
         """
         INSERT INTO "mvdmio"."migrations" (identifier, name, executed_at, scope)
         VALUES (202505181000, 'A again', NOW(), 'ScopeA')
         """,
         ct: CancellationToken);

      await duplicateInsert.Should().ThrowAsync<Exception>("the named unique index must reject a duplicate (scope, identifier) pair");
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

   /// <summary>
   ///    Serializes the full observable shape (columns in order + primary-key count + index definitions)
   ///    so two shapes can be compared with a single equality assertion.
   /// </summary>
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

   private sealed record ColumnRow(string ColumnName, string DataType, string IsNullable, int OrdinalPosition);
}
