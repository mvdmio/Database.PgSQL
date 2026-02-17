using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

public class MigrationTableConfigurationTests
{
   [Fact]
   public void Default_ReturnsDefaultConfiguration()
   {
      var config = MigrationTableConfiguration.Default;

      config.Schema.Should().Be(MigrationTableConfiguration.DEFAULT_SCHEMA);
      config.Table.Should().Be(MigrationTableConfiguration.DEFAULT_TABLE);
   }

   [Fact]
   public void NewInstance_HasDefaultValues()
   {
      var config = new MigrationTableConfiguration();

      config.Schema.Should().Be("mvdmio");
      config.Table.Should().Be("migrations");
   }

   [Fact]
   public void FullyQualifiedTableName_WithDefaults_ReturnsDefaultName()
   {
      var config = new MigrationTableConfiguration();

      config.FullyQualifiedTableName.Should().Be("\"mvdmio\".\"migrations\"");
   }

   [Fact]
   public void FullyQualifiedTableName_WithCustomSchemaAndTable_ReturnsCustomName()
   {
      var config = new MigrationTableConfiguration
      {
         Schema = "my_schema",
         Table = "schema_versions"
      };

      config.FullyQualifiedTableName.Should().Be("\"my_schema\".\"schema_versions\"");
   }

   [Fact]
   public void FullyQualifiedTableName_WithPublicSchema_ReturnsPublicSchemaName()
   {
      var config = new MigrationTableConfiguration
      {
         Schema = "public",
         Table = "migrations"
      };

      config.FullyQualifiedTableName.Should().Be("\"public\".\"migrations\"");
   }

   [Fact]
   public void Schema_CanBeSetViaInit()
   {
      var config = new MigrationTableConfiguration { Schema = "custom_schema" };

      config.Schema.Should().Be("custom_schema");
   }

   [Fact]
   public void Table_CanBeSetViaInit()
   {
      var config = new MigrationTableConfiguration { Table = "custom_table" };

      config.Table.Should().Be("custom_table");
   }

   [Fact]
   public void Default_IsSingleton()
   {
      var config1 = MigrationTableConfiguration.Default;
      var config2 = MigrationTableConfiguration.Default;

      config1.Should().BeSameAs(config2);
   }

   [Fact]
   public void Constants_HaveExpectedValues()
   {
      MigrationTableConfiguration.DEFAULT_SCHEMA.Should().Be("mvdmio");
      MigrationTableConfiguration.DEFAULT_TABLE.Should().Be("migrations");
   }
}
