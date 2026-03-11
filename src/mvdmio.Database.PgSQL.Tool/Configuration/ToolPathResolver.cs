namespace mvdmio.Database.PgSQL.Tool.Configuration;

/// <summary>
///    Resolves absolute tool paths from configuration.
/// </summary>
internal static class ToolPathResolver
{
   public static string GetProjectPath(ToolConfiguration config)
   {
      return Path.GetFullPath(Path.Combine(config.BasePath, config.Project));
   }

   public static string GetProjectDirectoryPath(ToolConfiguration config)
   {
      var projectPath = GetProjectPath(config);
      return projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
         ? Path.GetDirectoryName(projectPath)!
         : projectPath;
   }

   public static string GetMigrationsDirectoryPath(ToolConfiguration config)
   {
      return Path.GetFullPath(Path.Combine(config.BasePath, config.MigrationsDirectory));
   }

   public static string GetSchemasDirectoryPath(ToolConfiguration config)
   {
      return Path.GetFullPath(Path.Combine(config.BasePath, config.SchemasDirectory));
   }
}
