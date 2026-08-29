using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

public class SchemaBootstrapSelectorTests
{
   [Fact]
   public void ShouldApplySchema_WithEmptyVouchedScope_ReturnsTrue()
   {
      var result = SchemaBootstrapSelector.ShouldApplySchema(
         vouchedScopes: ["App.B"],
         scopesWithWatermark: new HashSet<string>(StringComparer.Ordinal) { "App.A" },
         headerLines: [new SchemaFileMigrationInfo(202601010000, "Baseline", "App.B")],
         targetIdentifier: null);

      result.Should().BeTrue();
   }

   [Fact]
   public void ShouldApplySchema_WhenEveryVouchedScopeHasWatermark_ReturnsFalse()
   {
      var result = SchemaBootstrapSelector.ShouldApplySchema(
         vouchedScopes: ["App.A", "App.A.Extra"],
         scopesWithWatermark: new HashSet<string>(StringComparer.Ordinal) { "App.A", "App.A.Extra" },
         headerLines: [new SchemaFileMigrationInfo(202601010000, "Baseline", "App.A")],
         targetIdentifier: null);

      result.Should().BeFalse();
   }

   [Fact]
   public void ShouldApplySchema_WhenHeaderExceedsTarget_ReturnsFalseForThatSchema()
   {
      var result = SchemaBootstrapSelector.ShouldApplySchema(
         vouchedScopes: ["App.B"],
         scopesWithWatermark: new HashSet<string>(StringComparer.Ordinal),
         headerLines: [new SchemaFileMigrationInfo(202605010000, "Baseline", "App.B")],
         targetIdentifier: 202604010000);

      result.Should().BeFalse();
   }

   [Fact]
   public void ShouldApplySchema_WhenHeaderAtOrBelowTarget_ReturnsTrue()
   {
      var result = SchemaBootstrapSelector.ShouldApplySchema(
         vouchedScopes: ["App.A"],
         scopesWithWatermark: new HashSet<string>(StringComparer.Ordinal),
         headerLines: [new SchemaFileMigrationInfo(202603010000, "Baseline", "App.A")],
         targetIdentifier: 202603010000);

      result.Should().BeTrue();
   }

   [Fact]
   public void FilterBaselinesToRecord_OmitsScopesThatAlreadyHadAWatermark()
   {
      var baselines = new[]
      {
         new SchemaFileMigrationInfo(202601010000, "A", "App.A"),
         new SchemaFileMigrationInfo(202602010000, "B", "App.B")
      };

      var recorded = SchemaBootstrapSelector.FilterBaselinesToRecord(
         baselines,
         scopesWithWatermarkBeforeRun: new HashSet<string>(StringComparer.Ordinal) { "App.A" },
         databaseWasGloballyEmpty: false);

      recorded.Should().ContainSingle()
         .Which.Should().Be(new SchemaFileMigrationInfo(202602010000, "B", "App.B"));
   }

   [Fact]
   public void FilterBaselinesToRecord_OnPopulatedDatabase_OmitsLegacyScopelessLines()
   {
      var baselines = new[]
      {
         new SchemaFileMigrationInfo(202601010000, "Legacy"),
         new SchemaFileMigrationInfo(202602010000, "B", "App.B")
      };

      var recorded = SchemaBootstrapSelector.FilterBaselinesToRecord(
         baselines,
         scopesWithWatermarkBeforeRun: new HashSet<string>(StringComparer.Ordinal) { "App.A" },
         databaseWasGloballyEmpty: false);

      recorded.Should().ContainSingle()
         .Which.Should().Be(new SchemaFileMigrationInfo(202602010000, "B", "App.B"));
   }

   [Fact]
   public void FilterBaselinesToRecord_OnGloballyEmptyDatabase_KeepsLegacyScopelessLines()
   {
      var baselines = new[]
      {
         new SchemaFileMigrationInfo(202601010000, "Legacy"),
         new SchemaFileMigrationInfo(202602010000, "B", "App.B")
      };

      var recorded = SchemaBootstrapSelector.FilterBaselinesToRecord(
         baselines,
         scopesWithWatermarkBeforeRun: new HashSet<string>(StringComparer.Ordinal),
         databaseWasGloballyEmpty: true);

      recorded.Should().HaveCount(2);
      recorded.Should().Contain(new SchemaFileMigrationInfo(202601010000, "Legacy"));
      recorded.Should().Contain(new SchemaFileMigrationInfo(202602010000, "B", "App.B"));
   }
}
