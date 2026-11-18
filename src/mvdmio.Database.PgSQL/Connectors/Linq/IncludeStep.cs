namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal sealed class IncludeStep
{
   private readonly Func<IQueryable, QueryRootRewriter, IQueryable> _apply;

   public IncludeStep(Func<IQueryable, QueryRootRewriter, IQueryable> apply)
   {
      ArgumentNullException.ThrowIfNull(apply);

      _apply = apply;
   }

   public IQueryable Apply(IQueryable queryable, QueryRootRewriter rewriter)
   {
      ArgumentNullException.ThrowIfNull(queryable);
      ArgumentNullException.ThrowIfNull(rewriter);

      return _apply.Invoke(queryable, rewriter);
   }
}
