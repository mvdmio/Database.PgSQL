namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal interface ISqlDiagnostics
{
   string RenderSql();

   string? LastSql();
}
