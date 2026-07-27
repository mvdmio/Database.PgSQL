namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Lets the test suite observe what a composed query translates to without putting SQL rendering on the
///    library's public surface.
/// </summary>
/// <remarks>
///    Untyped throughout: a query front-end may project into element types that are internal to its own assembly and so
///    cannot be named as a generic argument — an OData <c>$select</c> does exactly that.
/// </remarks>
internal static class QueryDiagnostics
{
   /// <summary>The SQL the query would translate to, without executing it. Only works for a sequence query.</summary>
   public static string RenderSql(IQueryable query)
   {
      return Diagnosing(query).RenderSql();
   }

   /// <summary>The SQL most recently sent to the database through the query's connection.</summary>
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
