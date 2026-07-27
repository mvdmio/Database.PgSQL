namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    What <see cref="QueryDiagnostics" /> needs from the query decorator, reachable without knowing the element type.
/// </summary>
/// <remarks>
///    Separate from <see cref="ITranslatedQueryable" /> because the two have different consumers: the rewriter needs the
///    expression and nothing else, and nothing that only rewrites should have to answer for SQL.
/// </remarks>
internal interface ISqlDiagnostics
{
   /// <summary>
   ///    Renders the SQL this query translates to, without executing it.
   /// </summary>
   string RenderSql();

   /// <summary>
   ///    The SQL most recently sent to the database, or null when none was.
   /// </summary>
   string? LastSql();
}
