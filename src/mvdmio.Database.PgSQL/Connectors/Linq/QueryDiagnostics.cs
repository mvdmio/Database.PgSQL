namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal static class QueryDiagnostics
{
   public static string RenderSql(IQueryable query)
   {
      return Diagnosing(query).RenderSql();
   }

   public static string? LastSql(IQueryable query)
   {
      return Diagnosing(query).LastSql();
   }

   private static ISqlDiagnostics Diagnosing(IQueryable query)
   {
      ArgumentNullException.ThrowIfNull(query);

      if (query is not ISqlDiagnostics diagnostics)
         throw new ArgumentException("The query was not produced by this library's query surface.", nameof(query));

      return diagnostics;
   }
}
