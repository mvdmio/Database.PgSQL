using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Dapper;
using Npgsql;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    Provides access to commonly used databases.
/// </summary>
[PublicAPI]
public sealed class DatabaseConnectionFactory : IDisposable, IAsyncDisposable
{
   private readonly Dictionary<string, NpgsqlDataSource> _dataSources = new Dictionary<string, NpgsqlDataSource>();
   private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
   
   /// <summary>
   /// Constructor.
   /// </summary>
   public DatabaseConnectionFactory()
   {
      DefaultConfig.EnsureInitialized();
   }
   
   /// <summary>
   ///    Creates a new database wrapper for the given connection string.
   /// </summary>
   public DatabaseConnection ForConnectionString(string connectionString, Action<NpgsqlDataSourceBuilder>? builderAction = null)
   {
      return GetConnection(connectionString, builderAction);
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      var exceptions = new List<Exception>();

      foreach (var dataSource in _dataSources)
      {
         try
         {
            await dataSource.Value.DisposeAsync();
         }
         catch (Exception ex)
         {
            exceptions.Add(ex);
         }
      }

      if (exceptions.Count == 1)
         throw exceptions[0];

      if (exceptions.Count > 1)
         throw new AggregateException(exceptions);
   }

   /// <inheritdoc />
   public void Dispose()
   {
      var exceptions = new List<Exception>();

      foreach (var dataSource in _dataSources)
      {
         try
         {
            dataSource.Value.Dispose();
         }
         catch (Exception ex)
         {
            exceptions.Add(ex);
         }
      }

      if (exceptions.Count == 1)
         throw exceptions[0];

      if (exceptions.Count > 1)
         throw new AggregateException(exceptions);
   }

   private DatabaseConnection GetConnection(string connectionString, Action<NpgsqlDataSourceBuilder>? builderAction = null)
   {
      if (_dataSources.TryGetValue(connectionString, out var dataSource))
         return new DatabaseConnection(dataSource);

      _lock.Wait();

      try
      {
         if (_dataSources.TryGetValue(connectionString, out dataSource))
            return new DatabaseConnection(dataSource);

         var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString) {
            ConnectionStringBuilder = {
               IncludeErrorDetail = true,
               LogParameters = true
            }
         };

         dataSourceBuilder.EnableDynamicJson();

         builderAction?.Invoke(dataSourceBuilder);
         dataSource = dataSourceBuilder.Build();
         
         _dataSources.Add(connectionString, dataSource);
         return new DatabaseConnection(dataSource);
      }
      finally
      {
         _lock.Release();
      }
   }
}