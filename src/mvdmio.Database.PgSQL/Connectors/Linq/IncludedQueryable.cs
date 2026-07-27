using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    The decorator handed back by a materialization operator: an ordinary composed query that also carries the type
///    of the relation it just included.
/// </summary>
/// <typeparam name="TEntity">The element type of the query.</typeparam>
/// <typeparam name="TProperty">The type of the relation property most recently included.</typeparam>
internal sealed class IncludedQueryable<TEntity, TProperty> : TranslatedQueryable<TEntity>, IIncludedQueryable<TEntity, TProperty>
{
   public IncludedQueryable(LinqQuerySource source, Expression expression)
      : base(source, expression)
   {
   }

   /// <remarks>
   ///    Composition that leaves the element type alone keeps the marker, which is what lets an operator sit between
   ///    the two halves of a chained materialization.
   /// </remarks>
   public override IQueryable<TOther> CreateQuery<TOther>(Expression expression)
   {
      ArgumentNullException.ThrowIfNull(expression);

      if (typeof(TOther) != typeof(TEntity))
         return base.CreateQuery<TOther>(expression);

      return (IQueryable<TOther>)(object)new IncludedQueryable<TEntity, TProperty>(Source, expression);
   }
}
