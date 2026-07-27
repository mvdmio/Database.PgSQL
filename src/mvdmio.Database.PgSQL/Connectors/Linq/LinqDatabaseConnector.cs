using JetBrains.Annotations;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.PostgreSQL;
using LinqToDB.Mapping;
using Npgsql;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Provides the deferred, composable query surface over a table definition.
/// </summary>
/// <remarks>
///    <para>
///       The adapter owns the query provider's context. The context never owns the connection —
///       <see cref="DatabaseConnection" /> remains the single owner of connection lifetime — and it is rebuilt
///       whenever the connection or the ambient transaction changes, so a query always executes against the state
///       current when it runs.
///    </para>
///    <para>
///       Composing a query touches no database. Executing one opens the connection if it is not already open, and
///       leaves it open: a queryable can be enumerated any number of times, so there is no point at which the adapter
///       could close it again. The connection closes with the <see cref="DatabaseConnection" />, the same way a
///       transaction holds it open until it ends.
///    </para>
/// </remarks>
[PublicAPI]
public sealed class LinqDatabaseConnector
{
   private readonly DatabaseConnection _databaseConnection;

   /// <summary>
   ///    Guards the context and the disposed flag. Always taken *after* the database connection's own lock — see
   ///    <see cref="ResolveContext" /> — because disposal arrives holding that one.
   /// </summary>
   private readonly object _lock = new();

   private BoundContext? _bound;
   private PostgresDialect _dialect = PostgresDialect.Latest;
   private bool _disposed;

   /// <summary>
   ///    The PostgreSQL dialect SQL is generated for. Defaults to the newest dialect the query surface knows about.
   /// </summary>
   public PostgresDialect Dialect
   {
      get => _dialect;
      set
      {
         lock (_lock)
         {
            if (_dialect == value)
               return;

            _dialect = value;
            DiscardContext();
         }
      }
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="LinqDatabaseConnector" /> class.
   /// </summary>
   /// <param name="databaseConnection">The database connection to query through.</param>
   public LinqDatabaseConnector(DatabaseConnection databaseConnection)
   {
      ArgumentNullException.ThrowIfNull(databaseConnection);

      _databaseConnection = databaseConnection;
   }

   /// <summary>
   ///    Registers custom conversions with the query surface, the counterpart of adding a Dapper type handler.
   ///    Like Dapper's handler registry this is process-wide, so call it once during startup and before the first
   ///    query runs.
   /// </summary>
   /// <param name="configure">A callback that receives the shared mapping schema.</param>
   public static void ConfigureMappingSchema(Action<MappingSchema> configure)
   {
      ArgumentNullException.ThrowIfNull(configure);

      QueryMappings.Configure(configure);
   }

   /// <summary>
   ///    Starts a deferred query over the table mapped to <typeparamref name="TEntity" />.
   /// </summary>
   /// <typeparam name="TEntity">A generated data type whose table mapping has been registered.</typeparam>
   /// <param name="commandTimeout">An optional timeout for the command the query executes as.</param>
   /// <returns>A queryable that translates to SQL when it is executed.</returns>
   public IQueryable<TEntity> Query<TEntity>(TimeSpan? commandTimeout = null)
      where TEntity : class
   {
      ObjectDisposedException.ThrowIf(_disposed, this);

      return new TranslatedQueryable<TEntity>(
         new LinqQuerySource(
            this,
            () => ResolveContext(commandTimeout).GetTable<TEntity>(),
            GetLastSql
         )
      );
   }

   internal void Dispose()
   {
      lock (_lock)
      {
         if (_disposed)
            return;

         _disposed = true;
         DiscardContext();
      }
   }

   internal async ValueTask DisposeAsync()
   {
      DataConnection? context;

      lock (_lock)
      {
         if (_disposed)
            return;

         _disposed = true;
         context = _bound?.Context;
         _bound = null;
      }

      if (context is not null)
         await context.DisposeAsync().ConfigureAwait(false);
   }

   /// <summary>
   ///    Returns the context to run against right now, rebuilding it when the connection or ambient transaction has
   ///    changed since it was built, and applying this query's command timeout to it.
   /// </summary>
   private DataConnection ResolveContext(TimeSpan? commandTimeout)
   {
      ObjectDisposedException.ThrowIf(_disposed, this);

      // Opening takes the database connection's lock, so it happens before ours: disposal arrives in the opposite
      // order — holding that lock, then taking ours — and nesting them the other way round here would deadlock.
      _databaseConnection.Open();

      var connection = _databaseConnection.Connection!;
      var transaction = _databaseConnection.Transaction;

      lock (_lock)
      {
         ObjectDisposedException.ThrowIf(_disposed, this);

         if (_bound is not null && !_bound.IsBoundTo(connection, transaction))
            DiscardContext();

         _bound ??= new BoundContext(CreateContext(connection, transaction), connection, transaction);

         // The provider takes a timeout per context, not per query, and one connection executes one query at a time,
         // so applying it here — at execution, not composition — gives it per-query scope.
         if (commandTimeout is null)
            _bound.Context.ResetCommandTimeout();
         else
            _bound.Context.CommandTimeout = (int)commandTimeout.Value.TotalSeconds;

         return _bound.Context;
      }
   }

   private DataConnection CreateContext(NpgsqlConnection connection, NpgsqlTransaction? transaction)
   {
      var dataProvider = PostgreSQLTools.GetDataProvider(ToProviderVersion(_dialect), connectionString: null, connection: connection);

      var options = transaction is null
         ? new DataOptions().UseConnection(dataProvider, connection, disposeConnection: false)
         : new DataOptions().UseTransaction(dataProvider, transaction);

      return new DataConnection(options.UseMappingSchema(QueryMappings.Schema));
   }

   /// <summary>The SQL most recently sent to the database, or null when none was. Never throws.</summary>
   private string? GetLastSql()
   {
      lock (_lock)
      {
         return _bound?.Context.LastQuery;
      }
   }

   private void DiscardContext()
   {
      _bound?.Context.Dispose();
      _bound = null;
   }

   private static PostgreSQLVersion ToProviderVersion(PostgresDialect dialect)
   {
      return dialect switch
      {
         PostgresDialect.V92 => PostgreSQLVersion.v92,
         PostgresDialect.V93 => PostgreSQLVersion.v93,
         PostgresDialect.V95 => PostgreSQLVersion.v95,
         PostgresDialect.V13 => PostgreSQLVersion.v13,
         PostgresDialect.V15 => PostgreSQLVersion.v15,
         PostgresDialect.V18 or PostgresDialect.Latest => PostgreSQLVersion.v18,
         _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Unknown PostgreSQL dialect.")
      };
   }
}
