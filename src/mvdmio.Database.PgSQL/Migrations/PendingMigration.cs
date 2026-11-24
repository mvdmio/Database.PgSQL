using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Migrations;

internal sealed record PendingMigration(IDbMigration Migration, long? ScopeWatermark, DeployPhase Phase)
{
   public bool IsOutOfOrder => Phase == DeployPhase.PreDeploy && ScopeWatermark is { } watermark && Migration.Identifier < watermark;
}
