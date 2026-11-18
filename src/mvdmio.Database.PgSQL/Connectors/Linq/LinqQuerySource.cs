namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal sealed class LinqQuerySource
{
   private readonly Func<IQueryable> _rootFactory;
   private readonly Func<string?> _lastSqlAccessor;

   public object Owner { get; }

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
