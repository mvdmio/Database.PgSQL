namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    The late-bound root of a composed query. Resolving is deferred to execution time so that a query composed
///    before a transaction opened still runs against the transaction that is current when it executes.
/// </summary>
internal sealed class LinqQuerySource
{
   private readonly Func<IQueryable> _rootFactory;
   private readonly Func<string?> _lastSqlAccessor;

   /// <param name="rootFactory">Produces the provider's table queryable against the connection state current at call time.</param>
   /// <param name="lastSqlAccessor">Returns the SQL most recently sent to the database, or null when none was. Must not throw.</param>
   public LinqQuerySource(Func<IQueryable> rootFactory, Func<string?> lastSqlAccessor)
   {
      ArgumentNullException.ThrowIfNull(rootFactory);
      ArgumentNullException.ThrowIfNull(lastSqlAccessor);

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
