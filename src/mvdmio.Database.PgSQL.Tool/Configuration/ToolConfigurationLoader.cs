using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace mvdmio.Database.PgSQL.Tool.Configuration;

/// <summary>
///    Loads tool configuration from the nearest configuration file.
/// </summary>
internal static class ToolConfigurationLoader
{
   public static ToolConfiguration Load()
   {
      var configFilePath = FindConfigFile();

      if (configFilePath is null)
         return new ToolConfiguration();

      var yaml = File.ReadAllText(configFilePath);
      var deserializer = new DeserializerBuilder()
         .WithNamingConvention(CamelCaseNamingConvention.Instance)
         .IgnoreUnmatchedProperties()
         .Build();

      var config = deserializer.Deserialize<ToolConfiguration>(yaml) ?? new ToolConfiguration();
      config.BasePath = Path.GetDirectoryName(configFilePath)!;

      return config;
   }

   private static string? FindConfigFile()
   {
      var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

      while (directory is not null)
      {
         var configPath = Path.Combine(directory.FullName, ToolConfiguration.CONFIG_FILE_NAME);
         if (File.Exists(configPath))
            return configPath;

         directory = directory.Parent;
      }

      return null;
   }
}
