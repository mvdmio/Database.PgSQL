using Dapper;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Connectors;
using mvdmio.Database.PgSQL.Connectors.Bulk;
using mvdmio.Database.PgSQL.Exceptions;
using Npgsql;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    Provides common methods for database access.
/// </summary>
[PublicAPI]
public class DatabaseConnection : IDisposable, IAsyncDisposable
{
   private readonly NpgsqlDataSource _datasource;

   private readonly SemaphoreSlim _connectionLock = new(1, 1);
   private NpgsqlConnection? _openConnection;

   internal NpgsqlConnection? Connection => _openConnection;

   /// <inheritdoc cref="DapperDatabaseConnector" />
   public DapperDatabaseConnector Dapper { get; }

   /// <inheritdoc cref="ManagementDatabaseConnector" />
   public ManagementDatabaseConnector Management { get; }

   /// <inheritdoc cref="BulkConnector" />
   public BulkConnector Bulk { get; }

   /// <inheritdoc cref="DatabaseConnectionInfo" />
   public DatabaseConnectionInfo Info { get; }

   private readonly SemaphoreSlim _transactionLock = new(1, 1);
   private bool _transactionOpenedConnection;
   internal NpgsqlTransaction? Transaction { get; private set; }

   /// <summary>
   ///    Create a new database connection for a database that is reachable with the given connection string.
   /// </summary>
   /// <param name="connectionString"></param>
   public DatabaseConnection(string connectionString)
      : this(new NpgsqlDataSourceBuilder(connectionString).Build())
   {
   }

   /// <summary>
   ///    Create a new database connection for the given datasource.
   /// </summary>
   public DatabaseConnection(NpgsqlDataSource dataSource)
   {
      _datasource = dataSource;

      Dapper = new DapperDatabaseConnector(this);
      Management = new ManagementDatabaseConnector(this);
      Bulk = new BulkConnector(this);
      Info = new DatabaseConnectionInfo(dataSource.ConnectionString);
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      await DisposeAsync(true);
      GC.SuppressFinalize(this);
   }

   protected virtual async ValueTask DisposeAsync(bool disposing)
   {
      if (!disposing)
         return;

      _transactionLock.Dispose();
      _connectionLock.Dispose();

      if (_openConnection is not null)
      {
         await _openConnection.DisposeAsync();
         _openConnection = null;
      }
   }

   /// <inheritdoc />
   public void Dispose()
   {
      Dispose(true);
      GC.SuppressFinalize(this);
   }

   protected virtual void Dispose(bool disposing)
   {
      if (!disposing)
         return;

      _transactionLock.Dispose();
      _connectionLock.Dispose();

      _openConnection?.Dispose();
      _openConnection = null;
   }

   /// <summary>
   ///    Manually open the connection. This is useful when you want to keep the connection open for multiple operations.
   ///    Make sure to close the connection when you're done with it.
   /// </summary>
   /// <returns>True if the connection was opened. False if it was already open.</returns>
   [MemberNotNull(nameof(_openConnection))]
   public bool Open()
   {
      _connectionLock.Wait();

      try
      {
         if (_openConnection is not null)
            return false;

         _openConnection = _datasource.OpenConnection();

         return true;
      }
      finally
      {
         _connectionLock.Release();
      }
   }

   /// <summary>
   ///    Manually open the connection. This is useful when you want to keep the connection open for multiple operations.
   ///    Make sure to close the connection when you're done with it.
   /// </summary>
   /// <returns>True if the connection was opened. False if it was already open.</returns>
   #pragma warning disable CS8774 // Member must have a non-null value when exiting
   [MemberNotNull(nameof(_openConnection))]
   public async Task<bool> OpenAsync(CancellationToken ct = default)
   {
      await _connectionLock.WaitAsync(ct);

      try
      {
         if (_openConnection is not null)
            return false;

         _openConnection = await _datasource.OpenConnectionAsync(ct);

         return true;
      }
      finally
      {
         _connectionLock.Release();
      }
   }
   #pragma warning restore CS8774

   /// <summary>
   ///    Manually close the connection. This is useful when you want to keep the connection open for multiple operations.
   /// </summary>
   public void Close()
   {
      _connectionLock.Wait();

      try
      {
         if (_openConnection is null)
            return;

         _openConnection.Close();
         _openConnection.Dispose();
         _openConnection = null;
      }
      finally
      {
         _connectionLock.Release();
      }
   }

   /// <summary>
   ///    Manually close the connection. This is useful when you want to keep the connection open for multiple operations.
   /// </summary>
   public async Task CloseAsync(CancellationToken ct = default)
   {
      await _connectionLock.WaitAsync(ct);

      try
      {
         if (_openConnection is null)
            return;

         await _openConnection.CloseAsync();
         await _openConnection.DisposeAsync();
         _openConnection = null;
      }
      finally
      {
         _connectionLock.Release();
      }
   }

   /// <summary>
   ///    Starts a new transaction on the connection if no transaction is already active.
   ///    Returns true when a transaction was started.
   /// </summary>
   /// <param name="isolationLevel">The isolation level to use for this transaction. Defaults to <see cref="IsolationLevel.ReadCommitted" /></param>
   /// <exception cref="InvalidOperationException">Thrown when a transaction has already been started on the connection.</exception>
   public bool BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
   {
      _transactionLock.Wait();

      try
      {
         if (Transaction is not null)
            return false;

         _transactionOpenedConnection = Open();
         Transaction = _openConnection.BeginTransaction(isolationLevel);
         return true;
      }
      finally
      {
         _transactionLock.Release();
      }
   }

   /// <summary>
   ///    Starts a new transaction on the connection.
   /// </summary>
   /// <param name="isolationLevel">The isolation level to use for this transaction. Defaults to <see cref="IsolationLevel.ReadCommitted" /></param>
   /// <param name="ct">An optional token to cancel the asynchronous operation. The default value is None.</param>
   /// <exception cref="InvalidOperationException">Thrown when a transaction has already been started on the connection.</exception>
   public async Task<bool> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default)
   {
      await _transactionLock.WaitAsync(ct);

      try
      {
         if (Transaction is not null)
            return false;

         _transactionOpenedConnection = await OpenAsync(ct);
         Transaction = await _openConnection.BeginTransactionAsync(isolationLevel, ct);
         return true;
      }
      finally
      {
         _transactionLock.Release();
      }
   }

   /// <summary>
   ///    Commits the transaction. Must be called after <see cref="BeginTransaction" />.
   /// </summary>
   /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
   public void CommitTransaction()
   {
      _transactionLock.Wait();

      try
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
            if(_transactionOpenedConnection)
               Close();
         }
      }
      finally
      {
         _transactionLock.Release();
      }
   }

   /// <summary>
   ///    Commits the transaction. Must be called after <see cref="BeginTransaction" />.
   /// </summary>
   /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
   public async Task CommitTransactionAsync(CancellationToken ct = default)
   {
      await _transactionLock.WaitAsync(ct);

      try
      {
         if (Transaction is null)
            throw new InvalidOperationException("No transaction active");

         try
         {
            await Transaction.CommitAsync(ct);
            await Transaction.DisposeAsync();
            Transaction = null;
         }
         finally
         {
            if (_transactionOpenedConnection)
               await CloseAsync(ct);
         }
      }
      finally
      {
         _transactionLock.Release();
      }
   }

   /// <summary>
   ///    Rolls back the transaction. Must be called after <see cref="BeginTransaction" />.
   /// </summary>
   /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
   public void RollbackTransaction()
   {
      _transactionLock.Wait();

      try
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
            if (_transactionOpenedConnection)
               Close();
         }
      }
      finally
      {
         _transactionLock.Release();
      }
   }

   /// <summary>
   ///    Rolls back the transaction. Must be called after <see cref="BeginTransaction" />.
   /// </summary>
   /// <exception cref="InvalidOperationException">Thrown when no transaction is active.</exception>
   public async Task RollbackTransactionAsync(CancellationToken ct = default)
   {
      await _transactionLock.WaitAsync(ct);

      try
      {
         if (Transaction is null)
            throw new InvalidOperationException("No transaction active");

         try
         {
            await Transaction.RollbackAsync(ct);
            await Transaction.DisposeAsync();
            Transaction = null;
         }
         finally
         {
            if (_transactionOpenedConnection)
               await CloseAsync(ct);
         }
      }
      finally
      {
         _transactionLock.Release();
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

   /// <summary>
   ///   Execute the given action in a transaction. If the action fails, the transaction will be rolled back.
   /// </summary>
   public async Task<T> InTransactionAsync<T>(Func<Task<T>> action)
   {
      var transactionStarted = await BeginTransactionAsync();

      try
      {
         var result = await action.Invoke();

         if(transactionStarted)
            await CommitTransactionAsync();

         return result;
      }
      catch
      {
         if (transactionStarted)
            await RollbackTransactionAsync();

         throw;
      }
   }

   /// <inheritdoc cref="NpgsqlConnection.Wait()"/>
   public void Wait(string channel)
   {
      var notificationConnection = _datasource.OpenConnection();

      try
      {
         notificationConnection.ExecuteAsync($"LISTEN {channel}");
         notificationConnection.Wait();
      }
      catch (Exception exception)
      {
         throw new QueryException($"LISTEN {channel}", exception);
      }
      finally
      {
         notificationConnection.Close();
         notificationConnection.Dispose();
      }
   }

   /// <inheritdoc cref="NpgsqlConnection.Wait(TimeSpan)"/>
   public bool Wait(string channel, TimeSpan timeout)
   {
      var notificationConnection = _datasource.OpenConnection();

      try
      {
         notificationConnection.ExecuteAsync($"LISTEN {channel}");
         return notificationConnection.Wait(timeout);
      }
      catch (Exception exception)
      {
         throw new QueryException($"LISTEN {channel}", exception);
      }
      finally
      {
         notificationConnection.Close();
         notificationConnection.Dispose();
      }
   }

   /// <inheritdoc cref="NpgsqlConnection.WaitAsync(CancellationToken)"/>
   public async Task WaitAsync(string channel, CancellationToken ct = default)
   {
      var notificationConnection = await _datasource.OpenConnectionAsync(ct);

      try
      {
         await notificationConnection.ExecuteAsync($"LISTEN {channel}");
         await notificationConnection.WaitAsync(ct);
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         throw; // Forward cancellation exceptions
      }
      catch (Exception exception)
      {
         throw new QueryException($"LISTEN {channel}", exception);
      }
      finally
      {
         await notificationConnection.CloseAsync();
         await notificationConnection.DisposeAsync();
      }
   }

   /// <inheritdoc cref="NpgsqlConnection.WaitAsync(TimeSpan, CancellationToken)"/>
   public async Task<bool> WaitAsync(string channel, TimeSpan timeout, CancellationToken ct = default)
   {
      var notificationConnection = await _datasource.OpenConnectionAsync(ct);

      try
      {
         await notificationConnection.ExecuteAsync($"LISTEN {channel}");
         return await notificationConnection.WaitAsync(timeout, ct);
      }
      catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
      {
         throw; // Forward cancellation exceptions
      }
      catch (Exception exception)
      {
         throw new QueryException($"LISTEN {channel}", exception);
      }
      finally
      {
         await notificationConnection.CloseAsync();
         await notificationConnection.DisposeAsync();
      }
   }

   internal void OpenConnectionAndExecute(string sql, Action<NpgsqlConnection> connectionDelegate)
   {
      ArgumentNullException.ThrowIfNull(sql);

      var connectionOpened = false;

      try
      {
         connectionOpened = Open();
         connectionDelegate.Invoke(_openConnection);
      }
      catch (Exception exception)
      {
         throw new QueryException(sql, exception);
      }
      finally
      {
         if (connectionOpened)
            Close();
      }
   }

   internal async Task OpenConnectionAndExecuteAsync(string sql, Func<NpgsqlConnection, Task> connectionDelegate, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(sql);

      var connectionOpened = false;

      try
      {
         connectionOpened = await OpenAsync(ct);
         await connectionDelegate.Invoke(_openConnection);
      }
      catch (Exception exception)
      {
         throw new QueryException(sql, exception);
      }
      finally
      {
         if (connectionOpened)
            await CloseAsync(ct);
      }
   }

   internal T OpenConnectionAndExecute<T>(string sql, Func<NpgsqlConnection, T> connectionDelegate)
   {
      ArgumentNullException.ThrowIfNull(sql);

      var connectionOpened = false;

      try
      {
         connectionOpened = Open();
         return connectionDelegate.Invoke(_openConnection);
      }
      catch (Exception exception)
      {
         throw new QueryException(sql, exception);
      }
      finally
      {
         if (connectionOpened)
            Close();
      }
   }

   internal async Task<T> OpenConnectionAndExecuteAsync<T>(string sql, Func<NpgsqlConnection, Task<T>> connectionDelegate, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(sql);

      var connectionOpened = false;

      try
      {
         connectionOpened = await OpenAsync(ct);
         return await connectionDelegate.Invoke(_openConnection);
      }
      catch (Exception exception)
      {
         throw new QueryException(sql, exception);
      }
      finally
      {
         if (connectionOpened)
            await CloseAsync(ct);
      }
   }
}
