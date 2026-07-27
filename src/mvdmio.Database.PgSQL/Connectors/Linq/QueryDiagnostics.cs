namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Lets the test suite observe what a composed query translates to without putting SQL rendering on the
///    library's public surface.
/// </summary>
internal static class QueryDiagnostics
{
   /// <summary>The SQL the query would translate to, without executing it. Only works for a sequence query.</summary>
   public static string RenderSql<TElement>(IQueryable<TElement> query)
   {
      return Decorating(query).RenderSql();
   }

   /// <summary>The SQL most recently sent to the database through the query's connection.</summary>
   public static string? LastSql<TElement>(IQueryable<TElement> query)
   {
      return Decorating(query).LastSql();
   }

   private static TranslatedQueryable<TElement> Decorating<TElement>(IQueryable<TElement> query)
   {
      ArgumentNullException.ThrowIfNull(query);

      if (query is not TranslatedQueryable<TElement> translated)
         throw new ArgumentException("The query was not produced by this library's query surface.", nameof(query));

      return translated;
   }
}
