using Npgsql;
using System.Data.Common;

namespace mvdmio.Database.PgSQL.Internal;

internal static class PoolSettingsResolver
{
   public const int DEFAULT_MAX_POOL_SIZE = 10;

   private static readonly string[] _maxPoolSizeKeywords = ["Maximum Pool Size", "MaxPoolSize"];
   private static readonly string[] _applicationNameKeywords = ["Application Name", "ApplicationName"];

   public static PoolSettings Resolve(string connectionString, string? entryAssemblyName, DatabaseConnectionFactorySettings settings)
   {
      var raw = new DbConnectionStringBuilder { ConnectionString = connectionString };
      var parsed = new NpgsqlConnectionStringBuilder(connectionString);

      var maxPoolSize = settings.MaxPoolSize
         ?? (HasAny(raw, _maxPoolSizeKeywords) ? parsed.MaxPoolSize : DEFAULT_MAX_POOL_SIZE);

      if (settings.ApplicationName is { } explicitName)
         return new PoolSettings(maxPoolSize, explicitName);

      var name = HasAny(raw, _applicationNameKeywords) ? parsed.ApplicationName : entryAssemblyName;

      return new PoolSettings(maxPoolSize, string.IsNullOrEmpty(name) ? null : name);
   }

   private static bool HasAny(DbConnectionStringBuilder raw, string[] keywords)
   {
      return keywords.Any(raw.ContainsKey);
   }
}

internal readonly record struct PoolSettings(int MaxPoolSize, string? ApplicationName);
