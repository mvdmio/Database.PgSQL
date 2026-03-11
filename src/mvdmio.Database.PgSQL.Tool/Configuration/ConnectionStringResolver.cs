namespace mvdmio.Database.PgSQL.Tool.Configuration;

/// <summary>
///    Resolves connection strings and environment names from tool configuration.
/// </summary>
internal static class ConnectionStringResolver
{
   public static string? ResolveConnectionString(ToolConfiguration config, string? connectionStringOverride, string? environmentOverride)
   {
      if (!string.IsNullOrWhiteSpace(connectionStringOverride))
         return connectionStringOverride;

      if (config.ConnectionStrings is null || config.ConnectionStrings.Count == 0)
         return null;

      if (environmentOverride is not null)
      {
         return config.ConnectionStrings.TryGetValue(environmentOverride, out var connectionString)
            ? connectionString
            : null;
      }

      return config.ConnectionStrings.Values.First();
   }

   public static string? ResolveEnvironmentName(ToolConfiguration config, string? connectionStringOverride, string? environmentOverride)
   {
      if (environmentOverride is not null)
         return environmentOverride;

      if (!string.IsNullOrWhiteSpace(connectionStringOverride))
         return null;

      if (config.ConnectionStrings is null || config.ConnectionStrings.Count == 0)
         return null;

      return config.ConnectionStrings.Keys.First();
   }

   public static string[] GetAvailableEnvironments(ToolConfiguration config)
   {
      return config.ConnectionStrings?.Keys.ToArray() ?? [];
   }
}
