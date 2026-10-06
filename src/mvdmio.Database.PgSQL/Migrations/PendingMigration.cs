using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    A migration the <see cref="PendingMigrationSelector" /> selected to run.
/// </summary>
/// <param name="Migration">The migration to run.</param>
/// <param name="ScopeWatermark">The highest identifier recorded for the migration's scope, or <see langword="null" /> when the scope has no rows.</param>
internal sealed record PendingMigration(IDbMigration Migration, long? ScopeWatermark)
{
   /// <summary>
   ///    <see langword="true" /> when the migration's scope has rows and its identifier is below the scope's watermark,
   ///    so it runs later than its number suggests.
   /// </summary>
   public bool IsOutOfOrder => ScopeWatermark is { } watermark && Migration.Identifier < watermark;
}
