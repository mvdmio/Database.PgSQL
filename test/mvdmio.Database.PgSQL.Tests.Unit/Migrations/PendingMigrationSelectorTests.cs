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
      // migrations. A global watermark would silently skip them; per-scope watermarks must not.
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
      pending.Select(x => x.Identifier).Should().Equal(202602010000, 202603010000);
   }

   [Fact]
   public void SelectPending_WithAllMigrationsBelowScopeWatermark_RunsNone()
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

      pending.Select(x => x.Identifier).Should().Equal(202602010000, 202602020000);
   }

   [Fact]
   public void SelectPending_WithNullScopeRows_ExcludesThemFromEveryWatermark()
   {
      // An un-backfilled legacy row must not act as a watermark for any concrete scope.
      var executed = new[] { Executed(202606010000, "Legacy", scope: null) };

      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA")
      };

      var pending = PendingMigrationSelector.SelectPending(executed, discovered);

      pending.Should().ContainSingle().Which.Identifier.Should().Be(202601010000);
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

      pending.Select(x => x.Identifier).Should().Equal(202601010000, 202602010000, 202603010000, 202604010000);
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

      pending.Select(x => x.Identifier).Should().Equal(202601010000, 202602010000);
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
