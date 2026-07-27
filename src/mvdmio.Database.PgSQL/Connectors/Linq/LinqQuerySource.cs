namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    The late-bound root of a composed query. Resolving is deferred to execution time so that a query composed
///    before a transaction opened still runs against the transaction that is current when it executes.
/// </summary>
internal sealed class LinqQuerySource
{
   private readonly Func<IQueryable> _rootFactory;
   private readonly Func<string?> _lastSqlAccessor;

   /// <summary>
   ///    The query surface this source was created by. One expression may name several sources — a correlated
   ///    subquery across two generated repositories does — and they can only be one query when they share an owner,
   ///    because that is what makes them share a connection and therefore a provider context.
   /// </summary>
   public object Owner { get; }

   /// <param name="owner">The query surface the source belongs to.</param>
   /// <param name="rootFactory">Produces the provider's table queryable against the connection state current at call time.</param>
   /// <param name="lastSqlAccessor">Returns the SQL most recently sent to the database, or null when none was. Must not throw.</param>
   public LinqQuerySource(object owner, Func<IQueryable> rootFactory, Func<string?> lastSqlAccessor)
   {
      ArgumentNullException.ThrowIfNull(owner);
      ArgumentNullException.ThrowIfNull(rootFactory);
      ArgumentNullException.ThrowIfNull(lastSqlAccessor);

      Owner = owner;
      _rootFactory = rootFactory;
      _lastSqlAccessor = lastSqlAccessor;
   }

   public IQueryable ResolveRoot()
   {
      return _rootFactory.Invoke();
   }

   public string? GetLastSql()
   {
      return _lastSqlAccessor.Invoke();
   }
}
