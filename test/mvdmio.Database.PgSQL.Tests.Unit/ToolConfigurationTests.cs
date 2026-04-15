using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tool.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace mvdmio.Database.PgSQL.Tests.Unit;

public class ToolConfigurationTests
{
   [Fact]
   public void Deserialize_WithConnectionStrings_ParsesCorrectly()
   {
      var yaml = """
         project: src/MyApp.Data
         migrationsDirectory: Migrations
         connectionStrings:
           local: Host=localhost;Database=mydb;Username=postgres;Password=secret
           acc: Host=acc-server;Database=mydb;Username=postgres;Password=secret
           prod: Host=prod-server;Database=mydb;Username=postgres;Password=secret
         """;

      var config = Deserialize(yaml);

      config.Project.Should().Be("src/MyApp.Data");
      config.MigrationsDirectory.Should().Be("Migrations");
      config.ConnectionStrings.Should().NotBeNull();
      config.ConnectionStrings.Should().HaveCount(3);
      config.ConnectionStrings!["local"].Should().Be("Host=localhost;Database=mydb;Username=postgres;Password=secret");
      config.ConnectionStrings!["acc"].Should().Be("Host=acc-server;Database=mydb;Username=postgres;Password=secret");
      config.ConnectionStrings!["prod"].Should().Be("Host=prod-server;Database=mydb;Username=postgres;Password=secret");
   }

   [Fact]
   public void Deserialize_WithNoConnectionStrings_ParsesWithNulls()
   {
      var yaml = """
         project: src/MyApp.Data
         migrationsDirectory: Migrations
         """;

      var config = Deserialize(yaml);

      config.Project.Should().Be("src/MyApp.Data");
      config.ConnectionStrings.Should().BeNull();
   }

   [Fact]
   public void Deserialize_WithSchemas_ParsesCorrectly()
   {
      var yaml = """
         schemas:
           - billing
           - public
         """;

      var config = Deserialize(yaml);

      config.Schemas.Should().BeEquivalentTo(["billing", "public"]);
   }

   [Fact]
   public void Deserialize_WithOldConnectionStringField_IgnoresItGracefully()
   {
      var yaml = """
         project: src/MyApp.Data
         migrationsDirectory: Migrations
         connectionString: Host=localhost;Database=mydb
         """;

      var config = Deserialize(yaml);

      config.Project.Should().Be("src/MyApp.Data");
      config.ConnectionStrings.Should().BeNull();
   }

   [Fact]
   public void Deserialize_WithOldDefaultEnvironmentField_IgnoresItGracefully()
   {
      var yaml = """
         project: src/MyApp.Data
         migrationsDirectory: Migrations
         defaultEnvironment: local
         connectionStrings:
           local: Host=localhost;Database=mydb
         """;

      var config = Deserialize(yaml);

      config.Project.Should().Be("src/MyApp.Data");
      config.ConnectionStrings.Should().NotBeNull();
      config.ConnectionStrings!["local"].Should().Be("Host=localhost;Database=mydb");
   }

   [Fact]
   public void Save_WithConnectionStrings_ProducesValidYaml()
   {
      var config = new ToolConfiguration
      {
         Project = "src/MyApp.Data",
         MigrationsDirectory = "Migrations",
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["prod"] = "Host=prod-server;Database=proddb"
         }
      };

      var serializer = new SerializerBuilder()
         .WithNamingConvention(CamelCaseNamingConvention.Instance)
         .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
         .Build();

      var yaml = serializer.Serialize(config);
      var roundTripped = Deserialize(yaml);

      roundTripped.Project.Should().Be("src/MyApp.Data");
      roundTripped.ConnectionStrings.Should().HaveCount(2);
      roundTripped.ConnectionStrings!["local"].Should().Be("Host=localhost;Database=localdb");
      roundTripped.ConnectionStrings!["prod"].Should().Be("Host=prod-server;Database=proddb");
   }

   [Fact]
   public void Save_WithNoConnectionStrings_OmitsNullValues()
   {
      var config = new ToolConfiguration
      {
         Project = "src/MyApp.Data",
         MigrationsDirectory = "Migrations"
      };

      var serializer = new SerializerBuilder()
         .WithNamingConvention(CamelCaseNamingConvention.Instance)
         .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
         .Build();

      var yaml = serializer.Serialize(config);

      yaml.Should().NotContain("connectionStrings");
   }

   [Fact]
   public void Save_WithSchemas_NormalizesAndOmitsEmptyEntries()
   {
      var tempDirectory = Path.Combine(Path.GetTempPath(), $"tool-config-tests-{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDirectory);

      try
      {
         var config = new ToolConfiguration
         {
            Schemas = [" billing ", string.Empty, "billing", "public", "   "]
         };

         config.Save(tempDirectory);

         var savedYaml = File.ReadAllText(Path.Combine(tempDirectory, ToolConfiguration.CONFIG_FILE_NAME));
         savedYaml.Should().Contain("schemas:");
         savedYaml.Should().Contain("- billing");
         savedYaml.Should().Contain("- public");
         savedYaml.Should().NotContain("- \"\"");

         config.Schemas.Should().BeEquivalentTo(["billing", "public"]);
      }
      finally
      {
         Directory.Delete(tempDirectory, true);
      }
   }

   private static ToolConfiguration Deserialize(string yaml)
   {
      var deserializer = new DeserializerBuilder()
         .WithNamingConvention(CamelCaseNamingConvention.Instance)
         .IgnoreUnmatchedProperties()
         .Build();

      return deserializer.Deserialize<ToolConfiguration>(yaml) ?? new ToolConfiguration();
   }
}
