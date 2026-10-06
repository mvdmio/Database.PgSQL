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
      // The core regression: scope B's migrations carry lower timestamps than scope A's already-executed
      // migrations. A global watermark would silently skip them; per-scope selection must not.
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

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

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

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

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

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, targetIdentifier: 202602020000);

      pending.Select(x => x.Migration.Identifier).Should().Equal(202602010000, 202602020000);
   }

   [Fact]
   public void SelectPending_WithNullScopeRows_ExcludesThemFromEveryBaselineAndWatermark()
   {
      // An un-backfilled legacy row must not act as a baseline or watermark for any concrete scope.
      var executed = new[] { Executed(202606010000, "Legacy", scope: null) };

      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

      var selected = pending.Should().ContainSingle().Subject;
      selected.Migration.Identifier.Should().Be(202601010000);
      selected.IsOutOfOrder.Should().BeFalse();
   }

   [Fact]
   public void SelectPending_WithNullScopeRowBelowFoldedMigration_DoesNotUnhideIt()
   {
      // A scope-less row lower than the scope's baseline must not become the baseline and unhide a folded migration.
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

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

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

      var pending = PendingMigrationSelector.SelectPending([], discovered);

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

      var pending = PendingMigrationSelector.SelectPending([], discovered);

      pending.Select(x => x.Migration.Identifier).Should().Equal(202601010000, 202602010000);
      pending.Should().OnlyContain(x => !x.IsOutOfOrder && x.ScopeWatermark == null);
   }

   [Fact]
   public void SelectPending_WithMigrationBetweenBaselineAndWatermark_SelectsItAsOutOfOrder()
   {
      // Replay of Issue #2: another branch's 202610061205 and 202610061300 ran first, then the branch carrying
      // 202610061047 merged. The scope's baseline row predates both branches, so 202610061047 must still run.
      var executed = new[]
      {
         Executed(202609010000, "Baseline", "mvdmio.Compliance.Db"),
         Executed(202610061205, "OtherBranchOne", "mvdmio.Compliance.Db"),
         Executed(202610061300, "OtherBranchTwo", "mvdmio.Compliance.Db")
      };

      var discovered = new IDbMigration[]
      {
         Migration(202610061047, "VoiceCallReplacesMeetingSession", "mvdmio.Compliance.Db"),
         Migration(202610061205, "OtherBranchOne", "mvdmio.Compliance.Db"),
         Migration(202610061300, "OtherBranchTwo", "mvdmio.Compliance.Db")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

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

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

      var selected = pending.Should().ContainSingle().Subject;
      selected.Migration.Identifier.Should().Be(202602010000);
      selected.IsOutOfOrder.Should().BeFalse();
      selected.ScopeWatermark.Should().Be(202601010000);
   }

   [Fact]
   public void SelectPending_WithMigrationBelowFirstRowOfNeverBootstrappedScope_DoesNotSelectIt()
   {
      // Accepted gap: the lowest row is the baseline whether or not a bootstrap wrote it, so a migration numbered
      // below the first row of a scope that was never bootstrapped stays skipped.
      var executed = new[]
      {
         Executed(202610061205, "OtherBranchOne", "mvdmio.Compliance.Db"),
         Executed(202610061300, "OtherBranchTwo", "mvdmio.Compliance.Db")
      };

      var discovered = new IDbMigration[]
      {
         Migration(202610061047, "VoiceCallReplacesMeetingSession", "mvdmio.Compliance.Db"),
         Migration(202610061205, "OtherBranchOne", "mvdmio.Compliance.Db"),
         Migration(202610061300, "OtherBranchTwo", "mvdmio.Compliance.Db")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

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

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

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

      var pending = PendingMigrationSelector.SelectPending(executed, discovered, targetIdentifier);

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

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

      pending.Select(x => (x.Migration.Identifier, x.IsOutOfOrder)).Should().Equal(
         (202602010000L, false),
         (202603010000L, true),
         (202604010000L, false),
         (202606010000L, false));
   }

   private static ExecutedMigrationModel Executed(long identifier, string name, string? scope)
   {
      return new ExecutedMigrationModel(identifier, name, DateTime.UtcNow, scope);
   }

   private static IDbMigration Migration(long identifier, string name, string scope)
   {
      return new FakeMigration(identifier, name, scope);
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
