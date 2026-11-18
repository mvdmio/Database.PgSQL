using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace mvdmio.Database.PgSQL.Tool.Configuration;

public sealed class ToolConfiguration
{
   public const string CONFIG_FILE_NAME = ".mvdmio-migrations.yml";

   public string Project { get; set; } = ".";

   public string MigrationsDirectory { get; set; } = "Migrations";

   public string SchemasDirectory { get; set; } = "Schemas";

   public Dictionary<string, string>? ConnectionStrings { get; set; }

   public List<string>? Schemas { get; set; }

   public List<string>? Scopes { get; set; }

   [YamlIgnore]
   public string BasePath { get; set; } = Directory.GetCurrentDirectory();

   public void Save(string directory)
   {
      Schemas = NormalizeNameList(Schemas);
      Scopes = NormalizeNameList(Scopes);

      var serializer = new SerializerBuilder()
         .WithNamingConvention(CamelCaseNamingConvention.Instance)
         .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)
         .Build();

      var yaml = serializer.Serialize(this);
      var filePath = Path.Combine(directory, CONFIG_FILE_NAME);
      File.WriteAllText(filePath, yaml);
   }

   internal static List<string>? NormalizeNameList(List<string>? names)
   {
      if (names is null)
         return null;

      var normalized = names
         .Select(name => name.Trim())
         .Where(name => name.Length > 0)
         .Distinct(StringComparer.Ordinal)
         .ToList();

      return normalized.Count > 0 ? normalized : null;
   }
}
