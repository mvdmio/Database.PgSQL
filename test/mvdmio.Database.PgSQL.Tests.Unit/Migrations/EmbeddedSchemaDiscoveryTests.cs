using System.Reflection;
using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

/// <summary>
///    Unit tests for <see cref="EmbeddedSchemaDiscovery"/>.
/// </summary>
public class EmbeddedSchemaDiscoveryTests
{
   [Fact]
   public void SchemaResourceExists_WithEmptyAssemblyArray_ReturnsFalse()
   {
      var result = EmbeddedSchemaDiscovery.SchemaResourceExists([], null);

      result.Should().BeFalse();
   }

   [Fact]
   public void SchemaResourceExists_WithAssemblyWithoutSchemas_ReturnsFalse()
   {
      // Use an assembly that doesn't have embedded schema resources
      var assembly = typeof(string).Assembly; // mscorlib/System.Private.CoreLib

      var result = EmbeddedSchemaDiscovery.SchemaResourceExists([assembly], null);

      result.Should().BeFalse();
   }

   [Fact]
   public void FindSchemaResource_WithEmptyAssemblyArray_ReturnsNull()
   {
      var result = EmbeddedSchemaDiscovery.FindSchemaResource([], null);

      result.Should().BeNull();
   }

   [Fact]
   public void FindSchemaResource_WithAssemblyWithoutSchemas_ReturnsNull()
   {
      var assembly = typeof(string).Assembly;

      var result = EmbeddedSchemaDiscovery.FindSchemaResource([assembly], null);

      result.Should().BeNull();
   }

   [Fact]
   public async Task ReadSchemaContentAsync_WithEmptyAssemblyArray_ReturnsNull()
   {
      var result = await EmbeddedSchemaDiscovery.ReadSchemaContentAsync([], null, TestContext.Current.CancellationToken);

      result.Should().BeNull();
   }

   [Fact]
   public async Task ReadSchemaContentAsync_WithAssemblyWithoutSchemas_ReturnsNull()
   {
      var assembly = typeof(string).Assembly;

      var result = await EmbeddedSchemaDiscovery.ReadSchemaContentAsync([assembly], null, TestContext.Current.CancellationToken);

      result.Should().BeNull();
   }

   [Fact]
   public void GetSchemaResourceName_WithEmptyAssemblyArray_ReturnsNull()
   {
      var result = EmbeddedSchemaDiscovery.GetSchemaResourceName([], null);

      result.Should().BeNull();
   }

   [Fact]
   public void GetSchemaResourceName_WithAssemblyWithoutSchemas_ReturnsNull()
   {
      var assembly = typeof(string).Assembly;

      var result = EmbeddedSchemaDiscovery.GetSchemaResourceName([assembly], null);

      result.Should().BeNull();
   }
}
