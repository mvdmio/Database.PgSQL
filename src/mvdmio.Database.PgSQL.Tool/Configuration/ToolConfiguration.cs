using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace mvdmio.Database.PgSQL.Tool.Configuration;

/// <summary>
///    Configuration model for the migration tool, loaded from .mvdmio-migrations.yml.
/// </summary>
internal sealed class ToolConfiguration
{
   /// <summary>
   ///    The name of the configuration file.
   /// </summary>
   public const string CONFIG_FILE_NAME = ".mvdmio-migrations.yml";

   /// <summary>
   ///    Path to the project containing migrations, relative to the config file location.
   /// </summary>
   public string Project { get; set; } = ".";

   /// <summary>
   ///    Output directory for new migration files, relative to the config file location.
   /// </summary>
   public string MigrationsDirectory { get; set; } = "Migrations";

   /// <summary>
   ///    Connection string for database operations.
   /// </summary>
   public string? ConnectionString { get; set; }

   /// <summary>
   ///    The directory containing the config file. Used to resolve relative paths.
   ///    Not serialized from YAML.
   /// </summary>
   [YamlIgnore]
   public string BasePath { get; private set; } = Directory.GetCurrentDirectory();

   /// <summary>
   ///    Resolves the absolute path to the project directory.
   /// </summary>
   public string GetProjectPath()
   {
      return Path.GetFullPath(Path.Combine(BasePath, Project));
   }

   /// <summary>
   ///    Resolves the absolute path to the migrations output directory.
   /// </summary>
   public string GetMigrationsDirectoryPath()
   {
      return Path.GetFullPath(Path.Combine(BasePath, MigrationsDirectory));
   }

   /// <summary>
   ///    Saves the configuration to a .mvdmio-migrations.yml file in the specified directory.
   /// </summary>
   /// <param name="directory">The directory to write the config file to.</param>
   public void Save(string directory)
   {
      var serializer = new SerializerBuilder()
         .WithNamingConvention(CamelCaseNamingConvention.Instance)
         .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
         .Build();

      var yaml = serializer.Serialize(this);
      var filePath = Path.Combine(directory, CONFIG_FILE_NAME);
      File.WriteAllText(filePath, yaml);
   }

   /// <summary>
   ///    Loads the configuration from the nearest .mvdmio-migrations.yml file,
   ///    searching from the current directory upward. Returns defaults if no file is found.
   /// </summary>
   public static ToolConfiguration Load()
   {
      var configFilePath = FindConfigFile();

      if (configFilePath is null)
      {
         return new ToolConfiguration();
      }

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
         var configPath = Path.Combine(directory.FullName, CONFIG_FILE_NAME);

         if (File.Exists(configPath))
            return configPath;

         directory = directory.Parent;
      }

      return null;
   }
}
