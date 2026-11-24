using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

public class PendingMigrationSelectorTests
{
   [Fact]
   public void SelectPending_WithTwoScopesAndInterleavedIdentifiers_RunsLowerTimestampScope()
   {
      var executed = new[]
      {
         Executed(202601010000, "A1", "ScopeA"),
         Executed(202606010000, "A2", "ScopeA")
      };

      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA"),
         Migration(202606010000, "A2", "ScopeA"),
         Migration(202602010000, "B1", "ScopeB"),
         Migration(202603010000, "B2", "ScopeB")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      pending.Should().HaveCount(2);
      pending.Select(x => x.Migration.Identifier).Should().Equal(202602010000, 202603010000);
   }

   [Fact]
   public void SelectPending_WithAllMigrationsBelowScopeBaseline_RunsNone()
   {
      var executed = new[] { Executed(202606010000, "Baseline", "ScopeA") };

      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA"),
         Migration(202602010000, "A2", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      pending.Should().BeEmpty();
   }

   [Fact]
   public void SelectPending_WithTargetIdentifier_AppliesGlobalCeilingPerScope()
   {
      var executed = new[] { Executed(202601010000, "A1", "ScopeA") };

      var discovered = new IDbMigration[]
      {
         Migration(202602010000, "A2", "ScopeA"),
         Migration(202603010000, "A3", "ScopeA"),
         Migration(202602020000, "B1", "ScopeB"),
         Migration(202604010000, "B2", "ScopeB")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy, targetIdentifier: 202602020000);

      pending.Select(x => x.Migration.Identifier).Should().Equal(202602010000, 202602020000);
   }

   [Fact]
   public void SelectPending_WithNullScopeRows_ExcludesThemFromEveryBaselineAndWatermark()
   {
      var executed = new[] { Executed(202606010000, "Legacy", scope: null) };

      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      var selected = pending.Should().ContainSingle().Subject;
      selected.Migration.Identifier.Should().Be(202601010000);
      selected.IsOutOfOrder.Should().BeFalse();
   }

   [Fact]
   public void SelectPending_WithNullScopeRowBelowFoldedMigration_DoesNotUnhideIt()
   {
      var executed = new[]
      {
         Executed(202501010000, "Legacy", scope: null),
         Executed(202606010000, "Baseline", "ScopeA")
      };

      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA"),
         Migration(202607010000, "A2", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      pending.Should().ContainSingle().Which.Migration.Identifier.Should().Be(202607010000);
   }

   [Fact]
   public void SelectPending_WithMultipleScopes_OrdersResultByIdentifier()
   {
      var discovered = new IDbMigration[]
      {
         Migration(202603010000, "B2", "ScopeB"),
         Migration(202601010000, "A1", "ScopeA"),
         Migration(202602010000, "B1", "ScopeB"),
         Migration(202604010000, "A2", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending([], discovered, DeployPhase.PreDeploy);

      pending.Select(x => x.Migration.Identifier).Should().Equal(202601010000, 202602010000, 202603010000, 202604010000);
   }

   [Fact]
   public void SelectPending_WithNoExecutedRows_RunsEverythingInOrder()
   {
      var discovered = new IDbMigration[]
      {
         Migration(202602010000, "A2", "ScopeA"),
         Migration(202601010000, "A1", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending([], discovered, DeployPhase.PreDeploy);

      pending.Select(x => x.Migration.Identifier).Should().Equal(202601010000, 202602010000);
      pending.Should().OnlyContain(x => !x.IsOutOfOrder && x.ScopeWatermark == null);
   }

   [Fact]
   public void SelectPending_WithMigrationBetweenBaselineAndWatermark_SelectsItAsOutOfOrder()
   {
      var executed = new[]
      {
         Executed(202609010000, "Baseline", "mvdmio.Compliance.Db"),
         Executed(202610061205, "OtherBranchOne", "mvdmio.Compliance.Db"),
         Executed(202610061300, "OtherBranchTwo", "mvdmio.Compliance.Db")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, ComplianceMigrations, DeployPhase.PreDeploy);

      var selected = pending.Should().ContainSingle().Subject;
      selected.Migration.Identifier.Should().Be(202610061047);
      selected.IsOutOfOrder.Should().BeTrue();
      selected.ScopeWatermark.Should().Be(202610061300);
   }

   [Fact]
   public void SelectPending_WithMigrationAboveWatermark_SelectsItInOrder()
   {
      var executed = new[] { Executed(202601010000, "A1", "ScopeA") };

      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA"),
         Migration(202602010000, "A2", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      var selected = pending.Should().ContainSingle().Subject;
      selected.Migration.Identifier.Should().Be(202602010000);
      selected.IsOutOfOrder.Should().BeFalse();
      selected.ScopeWatermark.Should().Be(202601010000);
   }

   [Fact]
   public void SelectPending_WithMigrationBelowFirstRowOfNeverBootstrappedScope_DoesNotSelectIt()
   {
      var executed = new[]
      {
         Executed(202610061205, "OtherBranchOne", "mvdmio.Compliance.Db"),
         Executed(202610061300, "OtherBranchTwo", "mvdmio.Compliance.Db")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, ComplianceMigrations, DeployPhase.PreDeploy);

      pending.Should().BeEmpty();
   }

   [Fact]
   public void SelectPending_WithRowOfSameIdentifierInOtherScope_StillSelectsMigration()
   {
      var executed = new[]
      {
         Executed(202601010000, "A1", "ScopeA"),
         Executed(202603010000, "A3", "ScopeA"),
         Executed(202602010000, "B1", "ScopeB")
      };

      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA"),
         Migration(202602010000, "A2", "ScopeA"),
         Migration(202603010000, "A3", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      var selected = pending.Should().ContainSingle().Subject;
      selected.Migration.Identifier.Should().Be(202602010000);
      selected.IsOutOfOrder.Should().BeTrue();
   }

   [Theory]
   [InlineData(202601310000, false)]
   [InlineData(202602010000, true)]
   [InlineData(202602150000, true)]
   public void SelectPending_WithTargetAroundOutOfOrderMigration_IncludesItOnlyAtOrAboveItsIdentifier(long targetIdentifier, bool expectSelected)
   {
      var executed = new[]
      {
         Executed(202601010000, "A1", "ScopeA"),
         Executed(202603010000, "A3", "ScopeA")
      };

      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA"),
         Migration(202602010000, "A2", "ScopeA"),
         Migration(202603010000, "A3", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy, targetIdentifier);

      if (expectSelected)
         pending.Should().ContainSingle().Which.Migration.Identifier.Should().Be(202602010000);
      else
         pending.Should().BeEmpty();
   }

   [Fact]
   public void SelectPending_WithOutOfOrderMigrationInOneScope_OrdersAcrossScopesByIdentifier()
   {
      var executed = new[]
      {
         Executed(202601010000, "A1", "ScopeA"),
         Executed(202605010000, "A5", "ScopeA")
      };

      var discovered = new IDbMigration[]
      {
         Migration(202606010000, "A6", "ScopeA"),
         Migration(202604010000, "B4", "ScopeB"),
         Migration(202603010000, "A3", "ScopeA"),
         Migration(202602010000, "B2", "ScopeB"),
         Migration(202601010000, "A1", "ScopeA"),
         Migration(202605010000, "A5", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      pending.Select(x => (x.Migration.Identifier, x.IsOutOfOrder)).Should().Equal(
         (202602010000L, false),
         (202603010000L, true),
         (202604010000L, false),
         (202606010000L, false));
   }

   [Fact]
   public void SelectPending_WithPreDeployPhase_SelectsNoPostDeployPart()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA"), Executed(202602010000, "TwoPart", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202602010000, "TwoPart", "ScopeA"), TwoPartMigration(202603010000, "NewTwoPart", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      pending.Select(x => (x.Migration.Identifier, x.Phase)).Should().Equal((202603010000L, DeployPhase.PreDeploy));
   }

   [Fact]
   public void SelectPending_WithPostDeployPhaseAndPreDeployRowPresent_SelectsThePostDeployPartOnly()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA"), Executed(202602010000, "TwoPart", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202602010000, "TwoPart", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PostDeploy);

      pending.Select(x => (x.Migration.Identifier, x.Phase)).Should().Equal((202602010000L, DeployPhase.PostDeploy));
   }

   [Fact]
   public void SelectPending_WithPostDeployPhaseAndPreDeployRowAbsent_SelectsNothing()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202602010000, "TwoPart", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PostDeploy);

      pending.Should().BeEmpty();
   }

   [Fact]
   public void SelectPending_WithPostDeployRowRecorded_SelectsNoPostDeployPart()
   {
      var executed = new[]
      {
         Executed(202601010000, "Baseline", "ScopeA"),
         Executed(202602010000, "TwoPart", "ScopeA"),
         Executed(202602010000, "TwoPart", "ScopeA", DeployPhase.PostDeploy)
      };
      var discovered = new[] { TwoPartMigration(202602010000, "TwoPart", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.All);

      pending.Should().BeEmpty();
   }

   [Fact]
   public void SelectPending_WithAllPhaseAndNewMigrationInRecordedScope_SelectsBothPartsPreDeployFirst()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202603010000, "Second", "ScopeA"), TwoPartMigration(202602010000, "First", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.All);

      pending.Select(x => (x.Migration.Identifier, x.Phase)).Should().Equal(
         (202602010000L, DeployPhase.PreDeploy),
         (202602010000L, DeployPhase.PostDeploy),
         (202603010000L, DeployPhase.PreDeploy),
         (202603010000L, DeployPhase.PostDeploy));
   }

   [Theory]
   [InlineData(DeployPhase.PreDeploy, new[] { DeployPhase.PreDeploy })]
   [InlineData(DeployPhase.PostDeploy, new DeployPhase[0])]
   [InlineData(DeployPhase.All, new[] { DeployPhase.PreDeploy, DeployPhase.PostDeploy })]
   public void SelectPending_WithFreshScope_SelectsPartsPerPhase(DeployPhase phase, DeployPhase[] expectedParts)
   {
      var discovered = new[] { TwoPartMigration(202602010000, "TwoPart", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending([], discovered, phase);

      pending.Select(x => x.Phase).Should().Equal(expectedParts);
   }

   [Theory]
   [InlineData(DeployPhase.PreDeploy)]
   [InlineData(DeployPhase.PostDeploy)]
   [InlineData(DeployPhase.All)]
   public void SelectPending_WithMigrationAtOrBelowBaseline_SelectsNeitherPart(DeployPhase phase)
   {
      var executed = new[] { Executed(202606010000, "Baseline", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202601010000, "Folded", "ScopeA"), TwoPartMigration(202606010000, "AtBaseline", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, phase);

      pending.Should().BeEmpty();
   }

   [Theory]
   [InlineData(DeployPhase.PreDeploy, new long[0])]
   [InlineData(DeployPhase.PostDeploy, new[] { 202601010000L })]
   [InlineData(DeployPhase.All, new[] { 202601010000L })]
   public void SelectPending_WithTwoPartMigrationThatRanAsTheScopesFirst_SelectsItsPostDeployPartOnly(DeployPhase phase, long[] expectedPostDeployIdentifiers)
   {
      var executed = new[] { Executed(202601010000, "First", "ScopeA", DeployPhase.PreDeploy) };
      var discovered = new[] { TwoPartMigration(202601010000, "First", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, phase);

      pending.Should().OnlyContain(x => x.Phase == DeployPhase.PostDeploy);
      pending.Select(x => x.Migration.Identifier).Should().Equal(expectedPostDeployIdentifiers);
   }

   [Fact]
   public void SelectPending_WithTwoPartMigrationThatRanAsTheScopesFirstAndItsPostDeployRow_SelectsNothing()
   {
      var executed = new[]
      {
         Executed(202601010000, "First", "ScopeA", DeployPhase.PreDeploy),
         Executed(202601010000, "First", "ScopeA", DeployPhase.PostDeploy)
      };
      var discovered = new[] { TwoPartMigration(202601010000, "First", "ScopeA") };

      PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.All).Should().BeEmpty();
   }

   [Fact]
   public void SelectWaitingPostDeployParts_WithTwoPartMigrationThatRanAsTheScopesFirst_ListsIt()
   {
      var executed = new[] { Executed(202601010000, "First", "ScopeA", DeployPhase.PreDeploy) };
      var discovered = new[] { TwoPartMigration(202601010000, "First", "ScopeA") };

      PendingMigrationSelector.SelectWaitingPostDeployParts(executed, discovered).Should().ContainSingle();
   }

   [Theory]
   [InlineData(DeployPhase.PreDeploy)]
   [InlineData(DeployPhase.PostDeploy)]
   [InlineData(DeployPhase.All)]
   public void SelectPending_WithTwoPartMigrationFoldedIntoSchemaBaselineRow_SelectsNeitherPart(DeployPhase phase)
   {
      var executed = new[] { new ExecutedMigrationModel(202601010000, "SchemaBaseline", DateTime.UtcNow, "ScopeA") };
      var discovered = new[] { TwoPartMigration(202601010000, "Folded", "ScopeA") };

      PendingMigrationSelector.SelectPending(executed, discovered, phase).Should().BeEmpty();
   }

   [Fact]
   public void ExecutedMigrationModel_ReadFromTheDatabase_EqualsAModelWithTheSamePublicValues()
   {
      var executedAtUtc = DateTime.UtcNow;
      var fromDatabase = new ExecutedMigrationModel(202601010000, "First", executedAtUtc, "ScopeA") { Phase = DeployPhase.PreDeploy, HasRecordedPhase = true };
      var fromConsumer = new ExecutedMigrationModel(202601010000, "First", executedAtUtc, "ScopeA") { Phase = DeployPhase.PreDeploy };

      fromDatabase.Should().Be(fromConsumer);
      fromDatabase.GetHashCode().Should().Be(fromConsumer.GetHashCode());
   }

   [Fact]
   public void SelectPending_WithRowWithoutPhase_CountsItAsThePreDeployRow()
   {
      var executed = new[]
      {
         new ExecutedMigrationModel(202601010000, "Baseline", DateTime.UtcNow, "ScopeA"),
         new ExecutedMigrationModel(202602010000, "TwoPart", DateTime.UtcNow, "ScopeA")
      };
      executed[0].Phase.Should().Be(0);
      var discovered = new[] { TwoPartMigration(202602010000, "TwoPart", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.All);

      pending.Select(x => x.Phase).Should().Equal(DeployPhase.PostDeploy);
   }

   [Theory]
   [InlineData(202601310000, 0)]
   [InlineData(202602010000, 2)]
   [InlineData(202603010000, 2)]
   public void SelectPending_WithTargetIdentifier_AppliesTheCeilingToBothParts(long targetIdentifier, int expectedPartCount)
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202602010000, "TwoPart", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.All, targetIdentifier);

      pending.Should().HaveCount(expectedPartCount);
   }

   [Fact]
   public void SelectPending_WithTargetBelowWaitingPostDeployPart_SelectsNothing()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA"), Executed(202603010000, "TwoPart", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202603010000, "TwoPart", "ScopeA") };

      PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PostDeploy, targetIdentifier: 202602010000).Should().BeEmpty();
      PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PostDeploy, targetIdentifier: 202603010000).Should().ContainSingle();
   }

   [Theory]
   [InlineData(DeployPhase.PreDeploy)]
   [InlineData(DeployPhase.PostDeploy)]
   [InlineData(DeployPhase.All)]
   public void SelectPending_WithMigrationWithoutPostDeployPart_NeverSelectsAPostDeployPart(DeployPhase phase)
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA"), Executed(202602010000, "UpOnly", "ScopeA") };
      var discovered = new[] { Migration(202602010000, "UpOnly", "ScopeA"), Migration(202603010000, "NewUpOnly", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, phase);

      pending.Should().NotContain(x => x.Phase == DeployPhase.PostDeploy);
   }

   [Fact]
   public void SelectPending_WithMigrationThatGainedAPostDeployPartAfterItRan_SelectsThePostDeployPart()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA"), Executed(202602010000, "GainedPart", "ScopeA", DeployPhase.PreDeploy) };
      var discovered = new[] { TwoPartMigration(202602010000, "GainedPart", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PostDeploy);

      pending.Should().ContainSingle().Which.Phase.Should().Be(DeployPhase.PostDeploy);
   }

   [Fact]
   public void SelectPending_WithPostDeployRowAboveEveryPreDeployRow_KeepsTheWatermarkOnPreDeployRows()
   {
      var executed = new[]
      {
         Executed(202601010000, "Baseline", "ScopeA"),
         Executed(202609010000, "Stray", "ScopeA", DeployPhase.PostDeploy)
      };
      var discovered = new[] { Migration(202602010000, "Late", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      var selected = pending.Should().ContainSingle().Subject;
      selected.ScopeWatermark.Should().Be(202601010000);
      selected.IsOutOfOrder.Should().BeFalse();
   }

   [Fact]
   public void SelectPending_WithPostDeployRowBelowEveryPreDeployRow_KeepsTheBaselineOnPreDeployRows()
   {
      var executed = new[]
      {
         Executed(202601010000, "Stray", "ScopeA", DeployPhase.PostDeploy),
         Executed(202606010000, "Baseline", "ScopeA")
      };
      var discovered = new[] { Migration(202602010000, "Folded", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.PreDeploy);

      pending.Should().BeEmpty();
   }

   [Fact]
   public void SelectPending_WithOutOfOrderTwoPartMigrationInAllPhase_FlagsOnlyThePreDeployPartOutOfOrder()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA"), Executed(202603010000, "Later", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202602010000, "Late", "ScopeA") };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, DeployPhase.All);

      pending.Select(x => (x.Phase, x.IsOutOfOrder)).Should().Equal((DeployPhase.PreDeploy, true), (DeployPhase.PostDeploy, false));
   }

   [Fact]
   public void SelectWaitingPostDeployParts_NamesPartsWithAPreDeployRowAndNoPostDeployRow()
   {
      var executed = new[]
      {
         Executed(202601010000, "Baseline", "ScopeA"),
         Executed(202602010000, "Waiting", "ScopeA"),
         Executed(202603010000, "Done", "ScopeA"),
         Executed(202603010000, "Done", "ScopeA", DeployPhase.PostDeploy),
         Executed(202604010000, "UpOnly", "ScopeA")
      };
      var discovered = new[]
      {
         TwoPartMigration(202601010000, "Baseline", "ScopeA"),
         TwoPartMigration(202602010000, "Waiting", "ScopeA"),
         TwoPartMigration(202603010000, "Done", "ScopeA"),
         Migration(202604010000, "UpOnly", "ScopeA"),
         TwoPartMigration(202605010000, "NotRunYet", "ScopeA")
      };

      var waiting = PendingMigrationSelector.SelectWaitingPostDeployParts(executed, discovered);

      waiting.Select(x => x.Identifier).Should().Equal(202602010000);
   }

   [Fact]
   public void SelectWaitingPostDeployParts_AppliesTheCeiling()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA"), Executed(202603010000, "Waiting", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202603010000, "Waiting", "ScopeA") };

      PendingMigrationSelector.SelectWaitingPostDeployParts(executed, discovered, targetIdentifier: 202602010000).Should().BeEmpty();
      PendingMigrationSelector.SelectWaitingPostDeployParts(executed, discovered, targetIdentifier: 202603010000).Should().ContainSingle();
   }

   [Fact]
   public void SelectBlockedPostDeployParts_NamesPartsWhosePreDeployPartIsStillPending()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA"), Executed(202602010000, "Waiting", "ScopeA") };
      var discovered = new[]
      {
         TwoPartMigration(202601010000, "Baseline", "ScopeA"),
         TwoPartMigration(202602010000, "Waiting", "ScopeA"),
         TwoPartMigration(202603010000, "Blocked", "ScopeA"),
         Migration(202604010000, "UpOnly", "ScopeA"),
         TwoPartMigration(202605010000, "BlockedInFreshScope", "ScopeB")
      };

      var blocked = PendingMigrationSelector.SelectBlockedPostDeployParts(executed, discovered);

      blocked.Select(x => x.Identifier).Should().Equal(202603010000, 202605010000);
   }

   [Fact]
   public void SelectBlockedPostDeployParts_AppliesTheCeiling()
   {
      var executed = new[] { Executed(202601010000, "Baseline", "ScopeA") };
      var discovered = new[] { TwoPartMigration(202603010000, "Blocked", "ScopeA") };

      PendingMigrationSelector.SelectBlockedPostDeployParts(executed, discovered, targetIdentifier: 202602010000).Should().BeEmpty();
      PendingMigrationSelector.SelectBlockedPostDeployParts(executed, discovered, targetIdentifier: 202603010000).Should().ContainSingle();
   }

   private static IDbMigration[] ComplianceMigrations =>
   [
      Migration(202610061047, "VoiceCallReplacesMeetingSession", "mvdmio.Compliance.Db"),
      Migration(202610061205, "OtherBranchOne", "mvdmio.Compliance.Db"),
      Migration(202610061300, "OtherBranchTwo", "mvdmio.Compliance.Db")
   ];

   private static ExecutedMigrationModel Executed(long identifier, string name, string? scope)
   {
      return new ExecutedMigrationModel(identifier, name, DateTime.UtcNow, scope);
   }

   private static ExecutedMigrationModel Executed(long identifier, string name, string? scope, DeployPhase phase)
   {
      return new ExecutedMigrationModel(identifier, name, DateTime.UtcNow, scope) { Phase = phase, HasRecordedPhase = true };
   }

   private static IDbMigration Migration(long identifier, string name, string scope)
   {
      return new FakeMigration(identifier, name, scope);
   }

   private static IDbMigration TwoPartMigration(long identifier, string name, string scope)
   {
      return new FakeTwoPartMigration(identifier, name, scope);
   }

   private sealed class FakeTwoPartMigration(long identifier, string name, string scope) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => name;
      public string Scope => scope;

      public Task PreDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }

      public Task PostDeployAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }

   private sealed class FakeMigration(long identifier, string name, string scope) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => name;
      public string Scope => scope;

      public Task UpAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }
}
