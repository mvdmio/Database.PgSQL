using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tool.Configuration;

namespace mvdmio.Database.PgSQL.Tests.Unit;

public class ConnectionStringResolverTests
{
   [Fact]
   public void ResolveConnectionString_WithConnectionStringOverride_ReturnsOverride()
   {
      var config = CreateConfig();

      var result = ConnectionStringResolver.ResolveConnectionString(config, "Host=override;Database=overridedb", null);

      result.Should().Be("Host=override;Database=overridedb");
   }

   [Fact]
   public void ResolveConnectionString_WithEnvironmentOverride_ReturnsMatchingConnectionString()
   {
      var config = CreateConfig();

      var result = ConnectionStringResolver.ResolveConnectionString(config, null, "prod");

      result.Should().Be("Host=prod-server;Database=proddb");
   }

   [Fact]
   public void ResolveConnectionString_WithNoOverrides_ReturnsFirstConfiguredEnvironment()
   {
      var config = CreateConfig();

      var result = ConnectionStringResolver.ResolveConnectionString(config, null, null);

      result.Should().Be("Host=localhost;Database=localdb");
   }

   [Fact]
   public void ResolveConnectionString_WithUnknownEnvironment_ReturnsNull()
   {
      var config = CreateConfig();

      var result = ConnectionStringResolver.ResolveConnectionString(config, null, "staging");

      result.Should().BeNull();
   }

   [Fact]
   public void ResolveConnectionString_WithNoConnectionStrings_ReturnsNull()
   {
      var result = ConnectionStringResolver.ResolveConnectionString(new ToolConfiguration(), null, null);

      result.Should().BeNull();
   }

   [Fact]
   public void ResolveEnvironmentName_WithEnvironmentOverride_ReturnsOverride()
   {
      var result = ConnectionStringResolver.ResolveEnvironmentName(CreateConfig(), null, "prod");

      result.Should().Be("prod");
   }

   [Fact]
   public void ResolveEnvironmentName_WithConnectionStringOverrideAndNoEnvironment_ReturnsNull()
   {
      var result = ConnectionStringResolver.ResolveEnvironmentName(CreateConfig(), "Host=override;Database=overridedb", null);

      result.Should().BeNull();
   }

   [Fact]
   public void ResolveEnvironmentName_WithNoOverrides_ReturnsFirstConfiguredEnvironment()
   {
      var result = ConnectionStringResolver.ResolveEnvironmentName(CreateConfig(), null, null);

      result.Should().Be("local");
   }

   [Fact]
   public void ResolveEnvironmentName_WithNoConnectionStrings_ReturnsNull()
   {
      var result = ConnectionStringResolver.ResolveEnvironmentName(new ToolConfiguration(), null, null);

      result.Should().BeNull();
   }

   [Fact]
   public void GetAvailableEnvironments_WithConnectionStrings_ReturnsNames()
   {
      var environments = ConnectionStringResolver.GetAvailableEnvironments(CreateConfig());

      environments.Should().BeEquivalentTo("local", "prod");
   }

   [Fact]
   public void GetAvailableEnvironments_WithNoConnectionStrings_ReturnsEmptyArray()
   {
      var environments = ConnectionStringResolver.GetAvailableEnvironments(new ToolConfiguration());

      environments.Should().BeEmpty();
   }

   private static ToolConfiguration CreateConfig()
   {
      return new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=localdb",
            ["prod"] = "Host=prod-server;Database=proddb"
         }
      };
   }
}
