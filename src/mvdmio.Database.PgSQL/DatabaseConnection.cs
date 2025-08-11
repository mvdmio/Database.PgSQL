using System.Data;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Connectors;
using mvdmio.Database.PgSQL.Exceptions;
using Npgsql;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    Provides common methods for database access.
/// </summary>
[PublicAPI]
public sealed class DatabaseConnection : IDisposable, IAsyncDisposable
{
   private readonly NpgsqlDataSource _datasource;

   private NpgsqlConnection? _openConnection;

   /// <inheritdoc cref="DapperDatabaseConnector" />
   public DapperDatabaseConnector Dapper { get; }

   /// <inheritdoc cref="ManagementDatabaseConnector" />
   public ManagementDatabaseConnector Management { get; }

   /// <inheritdoc cref="BulkConnector" />
   public BulkConnector Bulk { get; }

   internal NpgsqlTransaction? Transaction { get; private set; }

   /// <summary>
   ///   
   /// </summary>
   /// <param name="connectionString"></param>
   public DatabaseConnection(string connectionString)
      : this(new NpgsqlDataSourceBuilder(connectionString).Build())
   {  
   }
   
   /// <summary>
   ///    Create a new database object for a database that is reachable with the given connection string.
   /// </summary>
   public DatabaseConnection(NpgsqlDataSource dataSource)
   {
      _datasource = dataSource;
      
      Dapper = new DapperDatabaseConnector(this);
      Management = new ManagementDatabaseConnector(this);
      Bulk = new BulkConnector(this);
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      if (_openConnection is not null)
         await _openConnection.DisposeAsync();
   }

   /// <inheritdoc />
   public void Dispose()
   {
      _openConnection?.Dispose();
   }

   /// <summary>
   ///    Manually open the connection. This is useful when you want to keep the connection open for multiple operations.
   ///    Make sure to close the connection when you're done with it.
   /// </summary>
   /// <returns>True if the connection was opened. False if it was already open.</returns>
   [MemberNotNull(nameof(_openConnection))]
   public bool Open()
   {
      if (_openConnection is not null)
         return false;

      _openConnection = _datasource.OpenConnection();

      return true;
   }

   /// <summary>
   ///    Manually open the connection. This is useful when you want to keep the connection open for multiple operations.
   ///    Make sure to close the connection when you're done with it.
   /// </summary>
   /// <returns>True if the connection was opened. False if it was already open.</returns>
   #pragma warning disable CS8774 // Member must have a non-null value when exiting
   [MemberNotNull(nameof(_openConnection))]
   public async Task<bool> OpenAsync(CancellationToken cancellationToken = default)
   {
      if (_openConnection is not null)
         return false;

      _openConnection = await _datasource.OpenConnectionAsync(cancellationToken);

      return true;
   }
   #pragma warning restore CS8774

   /// <summary>
   ///    Manually close the connection. This is useful when you want to keep the connection open for multiple operations.
   /// </summary>
   public void Close()
   {
      if (_openConnection is null)
         return;

      _openConnection.Close();
      _openConnection.Dispose();
      _openConnection = null;
   }

   /// <summary>
   ///    Manually close the connection. This is useful when you want to keep the connection open for multiple operations.
   /// </summary>
   public async Task CloseAsync()
   {
      if (_openConnection is null)
         return;

      await _openConnection.CloseAsync();
      await _openConnection.DisposeAsync();
      _openConnection = null;
   }

   /// <summary>
   ///    Starts a new transaction on the connection if no transaction is already active.
   ///    Returns true when a transaction was started.
   /// </summary>
   /// <param name="isolationLevel">
   ///    The isolation level to use for this transaction. Defaults to
   ///    <see cref="IsolationLevel.ReadCommitted" />
   /// </param>
   /// <exception cref="InvalidOperationException">Thrown when a transaction has already been started on the connection.</exception>
   public bool BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
   {
      if (Transaction is not null)
         return false;

      Open();
      Transaction = _openConnection.BeginTransaction(isolationLevel);
      return true;
   }

   /// <summary>
   ///    Starts a new transaction on the connection.
   /// </summary>
   /// <param name="isolationLevel">
   ///    The isolation level to use for this transaction. Defaults to
   ///    <see cref="IsolationLevel.ReadCommitted" />
   /// </param>
   /// <exception cref="InvalidOperationException">Thrown when a transaction has already been started on the connection.</exception>
   public async Task<bool> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
   {
      if (Transaction is not null)
         return false;

      await OpenAsync();
      Transaction = _openConnection.BeginTransaction(isolationLevel);
      return true;
   }

   /// <summary>
   ///    Commits the transaction. Must be called after <see cref="BeginTransaction" />.
   /// </summary>
   /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
   public void CommitTransaction()
   {
      if (Transaction is null)
         throw new InvalidOperationException("No transaction active");

      try
      {
         Transaction.Commit();
         Transaction.Dispose();
         Transaction = null;
      }
      finally
      {
         Close();
      }
   }

   /// <summary>
   ///    Commits the transaction. Must be called after <see cref="BeginTransaction" />.
   /// </summary>
   /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
   public async Task CommitTransactionAsync()
   {
      if (Transaction is null)
         throw new InvalidOperationException("No transaction active");

      try
      {
         await Transaction.CommitAsync();
         await Transaction.DisposeAsync();
         Transaction = null;
      }
      finally
      {
         await CloseAsync();
      }
   }

   /// <summary>
   ///    Rolls back the transaction. Must be called after <see cref="BeginTransaction" />.
   /// </summary>
   /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
   public void RollbackTransaction()
   {
      if (Transaction is null)
         throw new InvalidOperationException("No transaction active");

      try
      {
         Transaction.Rollback();
         Transaction.Dispose();
         Transaction = null;
      }
      finally
      {
         Close();
      }
   }

   /// <summary>
   ///    Rolls back the transaction. Must be called after <see cref="BeginTransaction" />.
   /// </summary>
   /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
   public async Task RollbackTransactionAsync()
   {
      if (Transaction is null)
         throw new InvalidOperationException("No transaction active");

      try
      {
         await Transaction.RollbackAsync();
         await Transaction.DisposeAsync();
         Transaction = null;
      }
      finally
      {
         await CloseAsync();
      }
   }

   /// <summary>
   ///   Execute the given action in a transaction. If the action fails, the transaction will be rolled back.
   /// </summary>
   public void InTransaction(Action action)
   {
      var transactionStarted = BeginTransaction();
      try
      {
         action.Invoke();
         
         if(transactionStarted)
            CommitTransaction();
      }
      catch
      {
         if (transactionStarted)
            RollbackTransaction();

         throw;
      }
   }
   
   /// <summary>
   ///   Execute the given action in a transaction. If the action fails, the transaction will be rolled back.
   /// </summary>
   public async Task InTransactionAsync(Func<Task> action)
   {
      var transactionStarted = await BeginTransactionAsync();

      try
      {
         await action.Invoke();
         
         if(transactionStarted)
            await CommitTransactionAsync();
      }
      catch
      {
         if (transactionStarted)
            await RollbackTransactionAsync();
         
         throw;
      }
   }
   
   internal void OpenConnectionAndExecute(string sql, Action<NpgsqlConnection> connectionDelegate)
   {
      if (sql is null)
         throw new ArgumentNullException(nameof(sql));

      var connectionOpened = false;

      try
      {
         connectionOpened = Open();
         connectionDelegate.Invoke(_openConnection);
      }
      catch (Exception exception)
      {
         throw new DatabaseException(sql, exception);
      }
      finally
      {
         if (connectionOpened)
            Close();
      }
   }

   internal async Task OpenConnectionAndExecuteAsync(string sql, Func<NpgsqlConnection, Task> connectionDelegate, CancellationToken ct = default)
   {
      if (sql is null)
         throw new ArgumentNullException(nameof(sql));

      var connectionOpened = false;

      try
      {
         connectionOpened = await OpenAsync(ct);
         await connectionDelegate.Invoke(_openConnection);
      }
      catch (Exception exception)
      {
         throw new DatabaseException(sql, exception);
      }
      finally
      {
         if (connectionOpened)
            await CloseAsync();
      }
   }

   internal T OpenConnectionAndExecute<T>(string sql, Func<NpgsqlConnection, T> connectionDelegate)
   {
      if (sql is null)
         throw new ArgumentNullException(nameof(sql));

      var connectionOpened = false;

      try
      {
         connectionOpened = Open();
         return connectionDelegate.Invoke(_openConnection);
      }
      catch (Exception exception)
      {
         throw new DatabaseException(sql, exception);
      }
      finally
      {
         if (connectionOpened)
            Close();
      }
   }

   internal async Task<T> OpenConnectionAndExecuteAsync<T>(string sql, Func<NpgsqlConnection, Task<T>> connectionDelegate)
   {
      if (sql is null)
         throw new ArgumentNullException(nameof(sql));
      
      var connectionOpened = false;

      try
      {
         connectionOpened = await OpenAsync();
         return await connectionDelegate.Invoke(_openConnection);
      }
      catch (Exception exception)
      {
         throw new DatabaseException(sql, exception);
      }
      finally
      {
         if (connectionOpened)
            await CloseAsync();
      }
   }
}