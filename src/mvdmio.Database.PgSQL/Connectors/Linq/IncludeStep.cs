namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    One recorded request to materialize a relation, held as the provider call that adds it to a query.
/// </summary>
/// <remarks>
///    The call is captured at the point where the consumer composed it, which is the only place where the entity type
///    and the relation property's type are both known statically. Replaying it needs neither reflection nor a second
///    copy of the provider's overload set.
/// </remarks>
internal sealed class IncludeStep
{
   private readonly Func<IQueryable, QueryRootRewriter, IQueryable> _apply;

   /// <param name="apply">
   ///    Applies this step's eager-loading call to a query the provider owns. Every expression the step hands over must
   ///    go through the rewriter it is given first: a scoping lambda is consumer-written and is free to name another
   ///    query, whose decorator the provider cannot make sense of.
   /// </param>
   public IncludeStep(Func<IQueryable, QueryRootRewriter, IQueryable> apply)
   {
      ArgumentNullException.ThrowIfNull(apply);

      _apply = apply;
   }

   /// <summary>
   ///    Applies the step to <paramref name="queryable" />, which must be a query the provider produced.
   /// </summary>
   /// <param name="queryable">The provider's query to add the eager load to.</param>
   /// <param name="rewriter">Resolves the query decorators inside an expression against the provider's own roots.</param>
   /// <returns>The provider's query with this step applied.</returns>
   public IQueryable Apply(IQueryable queryable, QueryRootRewriter rewriter)
   {
      ArgumentNullException.ThrowIfNull(queryable);
      ArgumentNullException.ThrowIfNull(rewriter);

      return _apply.Invoke(queryable, rewriter);
   }
}
