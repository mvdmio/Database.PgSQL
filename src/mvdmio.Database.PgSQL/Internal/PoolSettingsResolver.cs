using Npgsql;
using System.Data.Common;

namespace mvdmio.Database.PgSQL.Internal;

/// <summary>
///    Decides the pool cap and the name of every data source the library builds: an explicit factory setting, then a
///    connection string keyword, then the library's default. Both the factory and the directly constructed
///    <see cref="DatabaseConnection" /> reach it through <see cref="DataSourceBuilderDefaults" />, so the two paths cannot
///    drift apart.
/// </summary>
/// <remarks>
///    Npgsql's own cap is 100 connections per pool. Several programs that share one Postgres server can then ask for more
///    connections than its <c>max_connections</c> allows, which is why the library caps every pool lower by default and
///    names it, so <c>pg_stat_activity</c> shows which program holds the slots.
/// </remarks>
internal static class PoolSettingsResolver
{
   /// <summary>
   ///    The cap on a pool's connections when the connection string sets none.
   /// </summary>
   public const int DEFAULT_MAX_POOL_SIZE = 10;

   // Every spelling Npgsql 10 accepts for each keyword. DbConnectionStringBuilder matches keys in any letter case, and
   // drops a keyword whose value is empty, exactly as Npgsql's own parsing does, so an empty keyword counts as absent.
   private static readonly string[] _maxPoolSizeKeywords = ["Maximum Pool Size", "MaxPoolSize"];
   private static readonly string[] _applicationNameKeywords = ["Application Name", "ApplicationName"];

   /// <summary>
   ///    Works out the cap and the name for a data source. Each value is decided on its own: an explicit factory setting
   ///    wins, next a keyword in the connection string whatever its value, and the default applies only when neither is
   ///    present. A keyword with an empty value counts as absent, because the connection string parser drops it.
   /// </summary>
   /// <param name="connectionString">The caller's connection string.</param>
   /// <param name="entryAssemblyName">The entry assembly's simple name, or <see langword="null" /> when there is none.</param>
   /// <param name="settings">The factory's explicit settings, or <see langword="null" /> when there are none.</param>
   /// <returns>
   ///    The cap and the name to apply. The name is <see langword="null" /> when nothing should be set. An explicit name
   ///    is returned exactly as given, even when it is empty.
   /// </returns>
   public static PoolSettings Resolve(string connectionString, string? entryAssemblyName, DatabaseConnectionFactorySettings? settings)
   {
      var raw = new DbConnectionStringBuilder { ConnectionString = connectionString };
      var parsed = new NpgsqlConnectionStringBuilder(connectionString);

      var maxPoolSize = settings?.MaxPoolSize
         ?? (HasAny(raw, _maxPoolSizeKeywords) ? parsed.MaxPoolSize : DEFAULT_MAX_POOL_SIZE);

      if (settings?.ApplicationName is { } explicitName)
         return new PoolSettings(maxPoolSize, explicitName);

      var name = HasAny(raw, _applicationNameKeywords) ? parsed.ApplicationName : entryAssemblyName;

      return new PoolSettings(maxPoolSize, string.IsNullOrEmpty(name) ? null : name);
   }

   private static bool HasAny(DbConnectionStringBuilder raw, string[] keywords)
   {
      return keywords.Any(raw.ContainsKey);
   }
}

/// <summary>
///    The cap and the name to give one data source.
/// </summary>
/// <param name="MaxPoolSize">The maximum number of connections in the pool.</param>
/// <param name="ApplicationName">The application and pool name, or <see langword="null" /> to leave it unset.</param>
internal readonly record struct PoolSettings(int MaxPoolSize, string? ApplicationName);
