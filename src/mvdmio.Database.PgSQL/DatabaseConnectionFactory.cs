using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Internal;
using Npgsql;
using System.Collections.Concurrent;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    Provides access to commonly used databases.
///    Data sources are cached per connection string and built lazily exactly once, so the factory is safe to call concurrently.
/// </summary>
/// <remarks>
///    Every data source the factory builds caps its pool at 10 connections and is named after the entry assembly. The name
///    shows in <c>pg_stat_activity.application_name</c> and is Npgsql's pool name in traces, logs and metrics. Each value
///    is decided on its own: a <see cref="DatabaseConnectionFactorySettings" /> value given to the constructor wins, then
///    a <c>Maximum Pool Size</c> or <c>Application Name</c> keyword in the connection string, then the default. When
///    there is no entry assembly, the default name stays unset.
///    <para>
///    Dispose only after all in-flight operations using data sources from this factory have completed.
///    Disposing the factory concurrently with active use (e.g. while another thread is calling <see cref="BuildConnection" /> or
///    <see cref="BuildDataSource" />, or while a <see cref="DatabaseConnection" /> handed out by this factory is still in use) is not supported.
///    </para>
/// </remarks>
[PublicAPI]
public sealed class DatabaseConnectionFactory : IDisposable, IAsyncDisposable
{
   private readonly ConcurrentDictionary<string, Lazy<NpgsqlDataSource>> _dataSources = new();
   private readonly DatabaseConnectionFactorySettings _settings;
   private volatile bool _disposed;

   /// <summary>
   ///    Creates a factory that gives every data source it builds the library's default pool cap and name.
   /// </summary>
   public DatabaseConnectionFactory()
      : this(new DatabaseConnectionFactorySettings()) { }

   /// <summary>
   ///    Creates a factory that gives every data source it builds the given pool cap and name, whichever caller builds it
   ///    first. A value the settings leave unset falls back to the connection string's keyword, then to the default.
   /// </summary>
   /// <param name="settings">The pool cap and name to apply to every data source.</param>
   /// <exception cref="ArgumentNullException"><paramref name="settings" /> is <see langword="null" />.</exception>
   public DatabaseConnectionFactory(DatabaseConnectionFactorySettings settings)
   {
      ArgumentNullException.ThrowIfNull(settings);

      _settings = settings;
   }

   /// <summary>
   ///   Builds a new data source for the given connection string.
   ///   Data sources are cached, so multiple calls with the same connection string will return the same instance.
   ///   The data source gets the factory's pool cap and name; see <see cref="DatabaseConnectionFactory" />.
   /// </summary>
   /// <param name="connectionString">The PostgreSQL connection string.</param>
   /// <param name="builderAction">
   ///    An optional action to configure the <see cref="NpgsqlDataSourceBuilder"/>. It runs after the factory's settings
   ///    and defaults are applied, so it can override them. It only runs when the data source is first built for this connection string.
   /// </param>
   /// <returns>A <see cref="DatabaseConnection"/> instance for the specified connection string.</returns>
   public NpgsqlDataSource BuildDataSource(string connectionString, Action<NpgsqlDataSourceBuilder>? builderAction = null)
   {
      return RetrieveOrCreate(connectionString, builderAction);
   }

   /// <summary>
   ///    Creates a new database wrapper for the given connection string.
   ///    Its data source gets the factory's pool cap and name; see <see cref="DatabaseConnectionFactory" />.
   /// </summary>
   /// <param name="connectionString">The PostgreSQL connection string.</param>
   /// <param name="builderAction">
   ///    An optional action to configure the <see cref="NpgsqlDataSourceBuilder"/>. It runs after the factory's settings
   ///    and defaults are applied, so it can override them. It only runs when the data source is first built for this connection string.
   /// </param>
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
            PoolDefaults.Apply(dataSourceBuilder, cs, _settings);

            builderAction?.Invoke(dataSourceBuilder);
            return dataSourceBuilder.Build();
         })
      );

      return dataSource.Value;
   }
}
