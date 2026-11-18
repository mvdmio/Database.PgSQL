using Npgsql;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Internal;

internal static class DataSourceBuilderDefaults
{
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
