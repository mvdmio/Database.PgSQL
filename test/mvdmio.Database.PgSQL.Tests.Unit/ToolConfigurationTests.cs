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
   public void ResolveConnectionString_WithConnectionStringOverride_ReturnsOverride()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb"
         }
      };

      var result = config.ResolveConnectionString("Host=override;Database=overridedb", null);

      result.Should().Be("Host=override;Database=overridedb");
   }

   [Fact]
   public void ResolveConnectionString_WithEnvironmentOverride_ReturnsMatchingConnectionString()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["prod"] = "Host=prod-server;Database=proddb"
         }
      };

      var result = config.ResolveConnectionString(null, "prod");

      result.Should().Be("Host=prod-server;Database=proddb");
   }

   [Fact]
   public void ResolveConnectionString_WithNoOverrides_ReturnsFirstConfiguredEnvironment()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["prod"] = "Host=prod-server;Database=proddb"
         }
      };

      var result = config.ResolveConnectionString(null, null);

      result.Should().Be("Host=localhost;Database=localdb");
   }

   [Fact]
   public void ResolveConnectionString_WithNoConnectionStrings_ReturnsNull()
   {
      var config = new ToolConfiguration();

      var result = config.ResolveConnectionString(null, null);

      result.Should().BeNull();
   }

   [Fact]
   public void ResolveConnectionString_WithEmptyConnectionStrings_ReturnsNull()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>()
      };

      var result = config.ResolveConnectionString(null, null);

      result.Should().BeNull();
   }

   [Fact]
   public void ResolveConnectionString_WithUnknownEnvironment_ReturnsNull()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb"
         }
      };

      var result = config.ResolveConnectionString(null, "staging");

      result.Should().BeNull();
   }

   [Fact]
   public void ResolveConnectionString_ConnectionStringOverrideTakesPriorityOverEnvironment()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["prod"] = "Host=prod-server;Database=proddb"
         }
      };

      var result = config.ResolveConnectionString("Host=override;Database=overridedb", "prod");

      result.Should().Be("Host=override;Database=overridedb");
   }

   [Fact]
   public void ResolveConnectionString_EnvironmentOverrideTakesPriorityOverFirstConfigured()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["acc"] = "Host=acc-server;Database=accdb"
         }
      };

      var result = config.ResolveConnectionString(null, "acc");

      result.Should().Be("Host=acc-server;Database=accdb");
   }

   [Fact]
   public void GetAvailableEnvironments_WithConnectionStrings_ReturnsNames()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost",
            ["acc"] = "Host=acc-server",
            ["prod"] = "Host=prod-server"
         }
      };

      var environments = config.GetAvailableEnvironments();

      environments.Should().BeEquivalentTo(["local", "acc", "prod"]);
   }

   [Fact]
   public void GetAvailableEnvironments_WithNoConnectionStrings_ReturnsEmptyArray()
   {
      var config = new ToolConfiguration();

      var environments = config.GetAvailableEnvironments();

      environments.Should().BeEmpty();
   }

   [Fact]
   public void Serialize_WithConnectionStrings_ProducesValidYaml()
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

      // Deserialize back and verify round-trip
      var roundTripped = Deserialize(yaml);

      roundTripped.Project.Should().Be("src/MyApp.Data");
      roundTripped.ConnectionStrings.Should().HaveCount(2);
      roundTripped.ConnectionStrings!["local"].Should().Be("Host=localhost;Database=localdb");
      roundTripped.ConnectionStrings!["prod"].Should().Be("Host=prod-server;Database=proddb");
   }

   [Fact]
   public void Serialize_WithNoConnectionStrings_OmitsNullValues()
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
   public void Deserialize_WithOldConnectionStringField_IgnoresItGracefully()
   {
      // Old format -- should be ignored via IgnoreUnmatchedProperties
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
      // Old format with defaultEnvironment -- should be ignored via IgnoreUnmatchedProperties
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
   public void ResolveEnvironmentName_WithEnvironmentOverride_ReturnsOverride()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["prod"] = "Host=prod-server;Database=proddb"
         }
      };

      var result = config.ResolveEnvironmentName(null, "prod");

      result.Should().Be("prod");
   }

   [Fact]
   public void ResolveEnvironmentName_WithConnectionStringOverrideAndNoEnvironment_ReturnsNull()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb"
         }
      };

      var result = config.ResolveEnvironmentName("Host=override;Database=overridedb", null);

      result.Should().BeNull();
   }

   [Fact]
   public void ResolveEnvironmentName_WithConnectionStringOverrideAndEnvironment_ReturnsEnvironment()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["prod"] = "Host=prod-server;Database=proddb"
         }
      };

      var result = config.ResolveEnvironmentName("Host=override;Database=overridedb", "prod");

      result.Should().Be("prod");
   }

   [Fact]
   public void ResolveEnvironmentName_WithNoOverrides_ReturnsFirstConfiguredEnvironment()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["prod"] = "Host=prod-server;Database=proddb"
         }
      };

      var result = config.ResolveEnvironmentName(null, null);

      result.Should().Be("local");
   }

   [Fact]
   public void ResolveEnvironmentName_WithNoConnectionStrings_ReturnsNull()
   {
      var config = new ToolConfiguration();

      var result = config.ResolveEnvironmentName(null, null);

      result.Should().BeNull();
   }

   [Fact]
   public void ResolveEnvironmentName_WithEmptyConnectionStrings_ReturnsNull()
   {
      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>()
      };

      var result = config.ResolveEnvironmentName(null, null);

      result.Should().BeNull();
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
