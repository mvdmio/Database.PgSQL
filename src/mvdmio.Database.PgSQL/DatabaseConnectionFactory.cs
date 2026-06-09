using JetBrains.Annotations;
using Npgsql;
using System.Collections.Concurrent;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    Provides access to commonly used databases.
///    Data sources are cached per connection string and built lazily exactly once, so the factory is safe to call concurrently.
/// </summary>
/// <remarks>
///    Dispose only after all in-flight operations using data sources from this factory have completed.
///    Disposing the factory concurrently with active use (e.g. while another thread is calling <see cref="BuildConnection" /> or
///    <see cref="BuildDataSource" />, or while a <see cref="DatabaseConnection" /> handed out by this factory is still in use) is not supported.
/// </remarks>
[PublicAPI]
public sealed class DatabaseConnectionFactory : IDisposable, IAsyncDisposable
{
   private readonly ConcurrentDictionary<string, Lazy<NpgsqlDataSource>> _dataSources = new();
   private volatile bool _disposed;

   /// <summary>
   ///   Builds a new data source for the given connection string.
   ///   Data sources are cached, so multiple calls with the same connection string will return the same instance.
   /// </summary>
   /// <param name="connectionString">The PostgreSQL connection string.</param>
   /// <param name="builderAction">An optional action to configure the <see cref="NpgsqlDataSourceBuilder"/>.</param>
   /// <returns>A <see cref="DatabaseConnection"/> instance for the specified connection string.</returns>
   public NpgsqlDataSource BuildDataSource(string connectionString, Action<NpgsqlDataSourceBuilder>? builderAction = null)
   {
      return RetrieveOrCreate(connectionString, builderAction);
   }

   /// <summary>
   ///    Creates a new database wrapper for the given connection string.
   /// </summary>
   /// <param name="connectionString">The PostgreSQL connection string.</param>
   /// <param name="builderAction">An optional action to configure the <see cref="NpgsqlDataSourceBuilder"/>.</param>
   /// <returns>A <see cref="DatabaseConnection"/> instance for the specified connection string.</returns>
   public DatabaseConnection BuildConnection(string connectionString, Action<NpgsqlDataSourceBuilder>? builderAction = null)
   {
      return new DatabaseConnection(RetrieveOrCreate(connectionString, builderAction));
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      _disposed = true;

      foreach (var dataSource in _dataSources.Values)
      {
         if (dataSource.IsValueCreated)
            await dataSource.Value.DisposeAsync();
      }
   }

   /// <inheritdoc />
   public void Dispose()
   {
      _disposed = true;

      foreach (var dataSource in _dataSources.Values)
      {
         if (dataSource.IsValueCreated)
            dataSource.Value.Dispose();
      }
   }

   private NpgsqlDataSource RetrieveOrCreate(string connectionString, Action<NpgsqlDataSourceBuilder>? builderAction = null)
   {
      ObjectDisposedException.ThrowIf(_disposed, this);

      var dataSource = _dataSources.GetOrAdd(
         connectionString,
         cs => new Lazy<NpgsqlDataSource>(() =>
         {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(cs)
            {
               ConnectionStringBuilder = {
                  IncludeErrorDetail = true,
                  LogParameters = true
               }
            };

            dataSourceBuilder.EnableDynamicJson();

            builderAction?.Invoke(dataSourceBuilder);
            return dataSourceBuilder.Build();
         })
      );

      return dataSource.Value;
   }
}
