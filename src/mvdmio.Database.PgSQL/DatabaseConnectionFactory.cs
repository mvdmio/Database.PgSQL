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
   private readonly Dictionary<string, NpgsqlDataSource> _dataSources = new();
   private readonly SemaphoreSlim _lock = new(1, 1);

   /// <summary>
   ///   Initializes a new instance of the <see cref="DatabaseConnectionFactory"/> class.
   /// </summary>
   public DatabaseConnectionFactory()
   {
      DefaultConfig.EnsureInitialized();
   }

   /// <summary>
   ///    Creates a new database wrapper for the given connection string.
   /// </summary>
   /// <param name="connectionString">The PostgreSQL connection string.</param>
   /// <param name="builderAction">An optional action to configure the <see cref="NpgsqlDataSourceBuilder"/>.</param>
   /// <returns>A <see cref="DatabaseConnection"/> instance for the specified connection string.</returns>
   public DatabaseConnection ForConnectionString(string connectionString, Action<NpgsqlDataSourceBuilder>? builderAction = null)
   {
      return GetConnection(connectionString, builderAction);
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      foreach (var dataSource in _dataSources)
         await dataSource.Value.DisposeAsync();

      _lock.Dispose();
   }

   /// <inheritdoc />
   public void Dispose()
   {
      foreach (var dataSource in _dataSources)
         dataSource.Value.Dispose();

      _lock.Dispose();
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
