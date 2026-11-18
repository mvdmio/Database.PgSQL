using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal sealed class IncludedQueryable<TEntity, TProperty> : TranslatedQueryable<TEntity>, IIncludedQueryable<TEntity, TProperty>
{
   public IncludedQueryable(LinqQuerySource source, Expression expression)
      : base(source, expression)
   {
   }

   public override IQueryable<TOther> CreateQuery<TOther>(Expression expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      if (typeof(TOther) != typeof(TEntity))
         return base.CreateQuery<TOther>(expression);

      return (IQueryable<TOther>)(object)new IncludedQueryable<TEntity, TProperty>(Source, expression);
   }
}
