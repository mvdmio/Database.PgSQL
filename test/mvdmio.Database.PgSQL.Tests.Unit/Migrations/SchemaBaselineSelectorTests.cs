using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

public class SchemaBaselineSelectorTests
{
   [Fact]
   public void SelectBaselines_WithVouchedScopedLines_ReturnsHighestIdentifierPerScope()
   {
      var headers = new[]
      {
         Header(
            "schema.sql",
            "App.A",
            vouchedScopes: ["App.A"],
            new SchemaFileMigrationInfo(202601010000, "First", "App.A"),
            new SchemaFileMigrationInfo(202602010000, "Second", "App.A"))
      };

      var result = SchemaBaselineSelector.SelectBaselines(headers);

      result.Baselines.Should().ContainSingle();
      result.Baselines[0].Should().Be(new SchemaFileMigrationInfo(202602010000, "Second", "App.A"));
      result.Rejected.Should().BeEmpty();
   }

   [Fact]
   public void SelectBaselines_WithForeignScopeLine_RejectsItAndRecordsNoBaseline()
   {
      var headers = new[]
      {
         Header(
            "schema.production.sql",
            "App.A",
            vouchedScopes: ["App.A"],
            new SchemaFileMigrationInfo(202601010000, "OwnTable", "App.A"),
            new SchemaFileMigrationInfo(202607101400, "ForeignTable", "App.B"))
      };

      var result = SchemaBaselineSelector.SelectBaselines(headers);

      result.Baselines.Should().ContainSingle();
      result.Baselines[0].Scope.Should().Be("App.A");

      result.Rejected.Should().ContainSingle();
      result.Rejected[0].HeaderLine.Should().Be(new SchemaFileMigrationInfo(202607101400, "ForeignTable", "App.B"));
      result.Rejected[0].ResourceName.Should().Be("schema.production.sql");
      result.Rejected[0].AssemblyName.Should().Be("App.A");
   }

   [Fact]
   public void SelectBaselines_WithForeignLineForScopeVouchedByAnotherFile_UsesOnlyTheVouchingFilesIdentifier()
   {
      var headers = new[]
      {
         Header(
            "schema.sql",
            "App.A",
            vouchedScopes: ["App.A"],
            new SchemaFileMigrationInfo(202601010000, "OwnTable", "App.A"),
            new SchemaFileMigrationInfo(202605010000, "GhostLine", "App.B")),
         Header(
            "schema.sql",
            "App.B",
            vouchedScopes: ["App.B"],
            new SchemaFileMigrationInfo(202603010000, "RealLine", "App.B"))
      };

      var result = SchemaBaselineSelector.SelectBaselines(headers);

      result.Baselines.Should().HaveCount(2);
      result.Baselines.Should().Contain(new SchemaFileMigrationInfo(202601010000, "OwnTable", "App.A"));
      result.Baselines.Should().Contain(new SchemaFileMigrationInfo(202603010000, "RealLine", "App.B"));

      result.Rejected.Should().ContainSingle();
      result.Rejected[0].HeaderLine.Identifier.Should().Be(202605010000);
   }

   [Fact]
   public void SelectBaselines_WithScopeVouchedThroughDiscoveredMigrations_AcceptsLine()
   {
      var headers = new[]
      {
         Header(
            "schema.sql",
            "App.A",
            vouchedScopes: ["App.A", "Custom.Scope"],
            new SchemaFileMigrationInfo(202601010000, "CustomScoped", "Custom.Scope"))
      };

      var result = SchemaBaselineSelector.SelectBaselines(headers);

      result.Baselines.Should().ContainSingle();
      result.Baselines[0].Scope.Should().Be("Custom.Scope");
      result.Rejected.Should().BeEmpty();
   }

   [Fact]
   public void SelectBaselines_WithLegacyScopelessLines_KeepsEachIdentifierRegardlessOfVouching()
   {
      var headers = new[]
      {
         Header(
            "schema.sql",
            "App.A",
            vouchedScopes: ["App.A"],
            new SchemaFileMigrationInfo(202601010000, "LegacyOne", Scope: null)),
         Header(
            "schema.sql",
            "App.B",
            vouchedScopes: ["App.B"],
            new SchemaFileMigrationInfo(202602010000, "LegacyTwo", Scope: null),
            new SchemaFileMigrationInfo(202602010000, "LegacyTwo", Scope: null))
      };

      var result = SchemaBaselineSelector.SelectBaselines(headers);

      result.Baselines.Should().HaveCount(2);
      result.Baselines.Should().Contain(new SchemaFileMigrationInfo(202601010000, "LegacyOne", Scope: null));
      result.Baselines.Should().Contain(new SchemaFileMigrationInfo(202602010000, "LegacyTwo", Scope: null));
      result.Rejected.Should().BeEmpty();
   }

   [Fact]
   public void SelectBaselines_OrdersBaselinesByIdentifier()
   {
      var headers = new[]
      {
         Header(
            "schema.sql",
            "App.A",
            vouchedScopes: ["App.A"],
            new SchemaFileMigrationInfo(202605010000, "Later", "App.A")),
         Header(
            "schema.sql",
            "App.B",
            vouchedScopes: ["App.B"],
            new SchemaFileMigrationInfo(202601010000, "Earlier", "App.B"))
      };

      var result = SchemaBaselineSelector.SelectBaselines(headers);

      result.Baselines.Select(baseline => baseline.Identifier).Should().BeInAscendingOrder();
   }

   [Fact]
   public void SelectBaselines_WithNoHeaders_ReturnsEmptyResult()
   {
      var result = SchemaBaselineSelector.SelectBaselines([]);

      result.Baselines.Should().BeEmpty();
      result.Rejected.Should().BeEmpty();
   }

   private static SchemaFileHeader Header(string resourceName, string assemblyName, string[] vouchedScopes, params SchemaFileMigrationInfo[] migrationVersions)
   {
      return new SchemaFileHeader(resourceName, assemblyName, migrationVersions, vouchedScopes);
   }
}
