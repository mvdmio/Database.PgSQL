using Dapper;
using JetBrains.Annotations;
using System.Data;
using System.Data.Common;

namespace mvdmio.Database.PgSQL.Connectors;

/// <summary>
///    Provides Database access methods from Dapper.
/// </summary>
/// <remarks>
///    Dapper documentation: https://github.com/DapperLib/Dapper/blob/main/Readme.md
/// </remarks>
[PublicAPI]
public sealed class DapperDatabaseConnector
{
   private readonly DatabaseConnection _databaseConnection;

   /// <summary>
   ///    Initializes a new instance of the <see cref="DapperDatabaseConnector"/> class.
   /// </summary>
   /// <param name="databaseConnection">The database connection to use.</param>
   public DapperDatabaseConnector(DatabaseConnection databaseConnection)
   {
      _databaseConnection = databaseConnection;
   }

   /// <inheritdoc cref="SqlMapper.Execute(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public int Execute(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.Execute(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.ExecuteAsync(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<int> ExecuteAsync(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.ExecuteAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.ExecuteReader(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public IDataReader ExecuteReader(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.ExecuteReader(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.ExecuteReaderAsync(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<DbDataReader> ExecuteReaderAsync(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.ExecuteReaderAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.ExecuteScalar(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public object? ExecuteScalar(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.ExecuteScalar(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.ExecuteScalarAsync(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<object?> ExecuteScalarAsync(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.ExecuteScalarAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.ExecuteScalar{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public T? ExecuteScalar<T>(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.ExecuteScalar<T>(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.ExecuteScalarAsync{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<T?> ExecuteScalarAsync<T>(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.ExecuteScalarAsync<T>(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.Query(IDbConnection,string,object,IDbTransaction,bool,int?,CommandType?)" />
   public IEnumerable<dynamic> Query(
      string sql,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null
   )
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.Query(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            buffered,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryAsync(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<IEnumerable<dynamic>> QueryAsync(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.Query{T}(IDbConnection,string,object,IDbTransaction,bool,int?,CommandType?)" />
   public IEnumerable<T> Query<T>(
      string sql,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null
   )
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.Query<T>(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            buffered,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.Query{TFirst,TSecond,TReturn}" />
   public IEnumerable<TReturn> Query<TFirst, TSecond, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null
   )
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.Query(
            sql,
            map,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            buffered,
            splitOn,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.Query{TFirst,TSecond,TThird,TReturn}" />
   public IEnumerable<TReturn> Query<TFirst, TSecond, TTHird, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TTHird, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null
   )
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.Query(
            sql,
            map,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            buffered,
            splitOn,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.Query{TFirst,TSecond,TThird,TFourth,TReturn}" />
   public IEnumerable<TReturn> Query<TFirst, TSecond, TTHird, TFourth, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TTHird, TFourth, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null
   )
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.Query(
            sql,
            map,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            buffered,
            splitOn,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.Query{TFirst,TSecond,TThird,TFourth,TFifth,TReturn}" />
   public IEnumerable<TReturn> Query<TFirst, TSecond, TTHird, TFourth, TFifth, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TTHird, TFourth, TFifth, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null
   )
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.Query(
            sql,
            map,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            buffered,
            splitOn,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.Query{TFirst,TSecond,TThird,TFourth,TFifth,TSixth,TReturn}" />
   public IEnumerable<TReturn> Query<TFirst, TSecond, TTHird, TFourth, TFifth, TSixth, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TTHird, TFourth, TFifth, TSixth, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null
   )
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.Query(
            sql,
            map,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            buffered,
            splitOn,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc
   ///    cref="SqlMapper.QueryAsync{TFirst,TSecond,TReturn}(IDbConnection,string,Func{TFirst,TSecond,TReturn},object,IDbTransaction,bool,string,int?,CommandType?)" />
   public async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            ),
            map,
            splitOn
         ),
         ct
      );
   }

   /// <inheritdoc
   ///    cref="SqlMapper.QueryAsync{TFirst,TSecond,TThird,TReturn}(IDbConnection,string,Func{TFirst,TSecond,TThird,TReturn},object,IDbTransaction,bool,string,int?,CommandType?)" />
   public async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TTHird, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TTHird, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            ),
            map,
            splitOn
         ),
         ct
      );
   }

   /// <inheritdoc
   ///    cref="SqlMapper.QueryAsync{TFirst,TSecond,TThird,TFourth,TReturn}(IDbConnection,string,Func{TFirst,TSecond,TThird,TFourth,TReturn},object,IDbTransaction,bool,string,int?,CommandType?)" />
   public async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TTHird, TFourth, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TTHird, TFourth, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            ),
            map,
            splitOn
         ),
         ct
      );
   }

   /// <inheritdoc
   ///    cref="SqlMapper.QueryAsync{TFirst,TSecond,TThird,TFourth,TFifth,TReturn}(IDbConnection,string,Func{TFirst,TSecond,TThird,TFourth,TFifth,TReturn},object,IDbTransaction,bool,string,int?,CommandType?)" />
   public async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TTHird, TFourth, TFifth, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TTHird, TFourth, TFifth, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            ),
            map,
            splitOn
         ),
         ct
      );
   }

   /// <inheritdoc
   ///    cref="SqlMapper.QueryAsync{TFirst,TSecond,TThird,TFourth,TFifth,TSixth,TReturn}(IDbConnection,string,Func{TFirst,TSecond,TThird,TFourth,TFifth,TSixth,TReturn},object,IDbTransaction,bool,string,int?,CommandType?)" />
   public async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TTHird, TFourth, TFifth, TSixth, TReturn>(
      string sql,
      string splitOn,
      Func<TFirst, TSecond, TTHird, TFourth, TFifth, TSixth, TReturn> map,
      IDictionary<string, object?>? parameters = null,
      bool buffered = true,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            ),
            map,
            splitOn
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryAsync{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<IEnumerable<T>> QueryAsync<T>(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryAsync<T>(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryFirst(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public dynamic QueryFirst(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.QueryFirst(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryFirstAsync(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<dynamic> QueryFirstAsync(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryFirstAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryFirst{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public T QueryFirst<T>(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.QueryFirst<T>(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryFirstAsync{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<T> QueryFirstAsync<T>(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryFirstAsync<T>(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryFirstOrDefault(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public dynamic? QueryFirstOrDefault(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.QueryFirstOrDefault(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryFirstOrDefaultAsync(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<dynamic?> QueryFirstOrDefaultAsync(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryFirstOrDefaultAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryFirstOrDefault{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public T? QueryFirstOrDefault<T>(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.QueryFirstOrDefault<T>(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryFirstOrDefaultAsync{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<T?> QueryFirstOrDefaultAsync<T>(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QueryFirstOrDefaultAsync<T>(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryMultiple(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public T QueryMultiple<T>(
      string sql,
      Func<SqlMapper.GridReader, T> resultInterpreterFunc,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null
   )
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection =>
         {
            var resultGrid = connection.QueryMultiple(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds
            );

            return resultInterpreterFunc.Invoke(resultGrid);
         }
      );
   }

   /// <inheritdoc cref="SqlMapper.QueryMultipleAsync(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<T> QueryMultipleAsync<T>(
      string sql,
      Func<SqlMapper.GridReader, T> resultInterpreterFunc,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection =>
         {
            var resultGrid = await connection.QueryMultipleAsync(
               new CommandDefinition(
                  sql,
                  new DynamicParameters(parameters),
                  _databaseConnection.Transaction,
                  (int?)commandTimeout?.TotalSeconds,
                  cancellationToken: ct
               )
            );

            return resultInterpreterFunc.Invoke(resultGrid);
         },
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QuerySingle(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public dynamic QuerySingle(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.QuerySingle(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.QuerySingleAsync(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<dynamic> QuerySingleAsync(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QuerySingleAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QuerySingle{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public T QuerySingle<T>(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.QuerySingle<T>(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.QuerySingleAsync{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<T> QuerySingleAsync<T>(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QuerySingleAsync<T>(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QuerySingleOrDefault(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public dynamic? QuerySingleOrDefault(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.QuerySingleOrDefault(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.QuerySingleOrDefaultAsync(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<dynamic?> QuerySingleOrDefaultAsync(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }

   /// <inheritdoc cref="SqlMapper.QuerySingleOrDefault{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public T? QuerySingleOrDefault<T>(string sql, IDictionary<string, object?>? parameters = null, TimeSpan? commandTimeout = null)
   {
      return _databaseConnection.OpenConnectionAndExecute(
         sql,
         connection => connection.QuerySingleOrDefault<T>(
            sql,
            new DynamicParameters(parameters),
            _databaseConnection.Transaction,
            (int?)commandTimeout?.TotalSeconds
         )
      );
   }

   /// <inheritdoc cref="SqlMapper.QuerySingleOrDefaultAsync{T}(IDbConnection,string,object,IDbTransaction,int?,CommandType?)" />
   public async Task<T?> QuerySingleOrDefaultAsync<T>(
      string sql,
      IDictionary<string, object?>? parameters = null,
      TimeSpan? commandTimeout = null,
      CancellationToken ct = default
   )
   {
      return await _databaseConnection.OpenConnectionAndExecuteAsync(
         sql,
         async connection => await connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(
               sql,
               new DynamicParameters(parameters),
               _databaseConnection.Transaction,
               (int?)commandTimeout?.TotalSeconds,
               cancellationToken: ct
            )
         ),
         ct
      );
   }
}
