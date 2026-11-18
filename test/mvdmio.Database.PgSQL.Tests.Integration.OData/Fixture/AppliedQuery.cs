using mvdmio.Database.PgSQL.Connectors.Linq;
using System.Collections;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

public sealed class AppliedQuery
{
   public IQueryable Query { get; }

   public long? TotalCount { get; }

   internal AppliedQuery(IQueryable query, long? totalCount)
   {
      ArgumentNullException.ThrowIfNull(query);

      Query = query;
      TotalCount = totalCount;
   }

   public string RenderSql()
   {
      return QueryDiagnostics.RenderSql(Query);
   }

   public string? LastSql()
   {
      return QueryDiagnostics.LastSql(Query);
   }

   public async Task<IReadOnlyList<TEntity>> RowsAsync<TEntity>(CancellationToken ct)
   {
      if (Query is not IQueryable<TEntity> typed)
         throw new InvalidOperationException($"The applied query projects into '{Query.ElementType}', not '{typeof(TEntity)}'.");

      return await typed.ToListAsync(ct);
   }

   public IReadOnlyList<ProjectedRow> ProjectedRows()
   {
      return ((IEnumerable)Query).Cast<object>().Select(ProjectedRow.From).ToList();
   }
}
