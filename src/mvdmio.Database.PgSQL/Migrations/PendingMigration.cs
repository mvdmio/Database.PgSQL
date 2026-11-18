using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Migrations;

internal sealed record PendingMigration(IDbMigration Migration, long? ScopeWatermark)
{
   public bool IsOutOfOrder => ScopeWatermark is { } watermark && Migration.Identifier < watermark;
}
