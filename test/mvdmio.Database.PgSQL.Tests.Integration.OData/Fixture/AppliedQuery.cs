using Microsoft.AspNetCore.OData.Query.Wrapper;
using mvdmio.Database.PgSQL.Connectors.Linq;
using System.Collections;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    A queryable with OData query options applied, plus the two things a conformance test asserts on: the rows it
///    returns and the SQL it sends.
/// </summary>
public sealed class AppliedQuery
{
   /// <summary>The composed queryable. Untyped because <c>$select</c> and <c>$apply</c> change the element type.</summary>
   public IQueryable Query { get; }

   /// <summary>The count OData resolved for <c>$count=true</c>, or null when the query string did not ask for one.</summary>
   public long? TotalCount { get; }

   internal AppliedQuery(IQueryable query, long? totalCount)
   {
      ArgumentNullException.ThrowIfNull(query);

      Query = query;
      TotalCount = totalCount;
   }

   /// <summary>
   ///    The SQL the composed query translates to, without executing it. A method rather than a property because it
   ///    does the translation on every call, and throws when there is no translation to give.
   /// </summary>
   public string RenderSql()
   {
      return QueryDiagnostics.RenderSql(Query);
   }

   /// <summary>The SQL most recently sent to the database through this query's connection.</summary>
   public string? LastSql()
   {
      return QueryDiagnostics.LastSql(Query);
   }

   /// <summary>Materializes the query when the element type is unchanged.</summary>
   public async Task<IReadOnlyList<TEntity>> RowsAsync<TEntity>(CancellationToken ct)
   {
      if (Query is not IQueryable<TEntity> typed)
         throw new InvalidOperationException($"The applied query projects into '{Query.ElementType}', not '{typeof(TEntity)}'.");

      return await typed.ToListAsync(ct);
   }

   /// <summary>
   ///    Materializes a projected query as the name-value pairs it produced. Needed because <c>$select</c> and
   ///    <c>$apply</c> project into OData's own wrapper types, which are internal to its assembly and so cannot be
   ///    named here.
   /// </summary>
   public IReadOnlyList<IDictionary<string, object?>> ProjectedRows()
   {
      // Enumerable.Cast, not Queryable.Cast: the projection has already happened in SQL and the wrappers only need
      // boxing on the way out. Composing a Cast onto the queryable would push it at the provider instead.
      return ((IEnumerable)Query).Cast<object>().Select(ToValues).ToList();
   }

   private static IDictionary<string, object?> ToValues(object row)
   {
      return row switch
      {
         ISelectExpandWrapper selected => selected.ToDictionary().ToDictionary(x => x.Key, x => (object?)x.Value, StringComparer.Ordinal),
         DynamicTypeWrapper aggregated => aggregated.Values.ToDictionary(x => x.Key, x => (object?)x.Value, StringComparer.Ordinal),
         _ => throw new InvalidOperationException($"'{row.GetType()}' is not one of OData's projection wrappers.")
      };
   }
}
