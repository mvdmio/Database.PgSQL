using Npgsql;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Internal;

/// <summary>
///    Creates the builder for every data source the library builds, with what the factory and the directly constructed
///    <see cref="DatabaseConnection" /> share already applied. Both paths start here, so they cannot drift apart, and a
///    caller's builder action can only run on the returned builder, so it always runs after these defaults.
/// </summary>
/// <remarks>
///    Dynamic JSON is enabled on both paths, so which construction path a consumer took stops changing what a JSON column
///    does. Aligned by enabling it on both rather than by removing it from one: enabling it only widens what a parameter
///    may hold, whereas taking it away could break a caller already relying on it.
/// </remarks>
internal static class DataSourceBuilderDefaults
{
   /// <summary>
   ///    Creates a builder from the caller's connection string, with dynamic JSON enabled and the pool cap and name that
   ///    <see cref="PoolSettingsResolver" /> decides. The name goes to both the connection string's
   ///    <c>Application Name</c> and the builder's <see cref="NpgsqlDataSourceBuilder.Name" />, so the two always agree.
   /// </summary>
   /// <param name="connectionString">The caller's connection string.</param>
   /// <param name="settings">The factory's settings. A value left unset there falls through to the keyword or the default.</param>
   /// <returns>A builder with the shared defaults applied, ready for the caller's own configuration.</returns>
   public static NpgsqlDataSourceBuilder Create(string connectionString, DatabaseConnectionFactorySettings settings)
   {
      var builder = new NpgsqlDataSourceBuilder(connectionString);
      builder.EnableDynamicJson();

      var resolved = PoolSettingsResolver.Resolve(connectionString, Assembly.GetEntryAssembly()?.GetName().Name, settings);
      builder.ConnectionStringBuilder.MaxPoolSize = resolved.MaxPoolSize;

      if (resolved.ApplicationName is not null)
      {
         builder.ConnectionStringBuilder.ApplicationName = resolved.ApplicationName;
         builder.Name = resolved.ApplicationName;
      }

      return builder;
   }
}
