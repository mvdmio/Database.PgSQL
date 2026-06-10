using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

#pragma warning disable CS0618 // ScopeBackfillMatcher is obsolete by design; it is removed in the next major version.
public class ScopeBackfillMatcherTests
{
   [Fact]
   public void Match_WithRowMatchedByIdentifier_AssignsTheMigrationScope()
   {
      var executed = new[] { Executed(202601010000, "A1", scope: null) };
      var discovered = new IDbMigration[] { Migration(202601010000, "A1", "ScopeA") };

      var result = ScopeBackfillMatcher.Match(executed, discovered);

      result.Assignments.Should().ContainSingle().Which.Should().Be(new ScopeAssignment(202601010000, "ScopeA"));
      result.Unattributed.Should().BeEmpty();
   }

   [Fact]
   public void Match_WithAlreadyScopedRows_LeavesThemUntouched()
   {
      var executed = new[] { Executed(202601010000, "A1", "ScopeB") };
      var discovered = new IDbMigration[] { Migration(202601010000, "A1", "ScopeA") };

      var result = ScopeBackfillMatcher.Match(executed, discovered);

      result.Assignments.Should().BeEmpty();
      result.Unattributed.Should().BeEmpty();
   }

   [Fact]
   public void Match_WithNoMatchingDiscoveredMigration_ReportsRowAsUnattributed()
   {
      var executed = new[] { Executed(202601010000, "Unknown", scope: null) };
      var discovered = new IDbMigration[] { Migration(202602010000, "A1", "ScopeA") };

      var result = ScopeBackfillMatcher.Match(executed, discovered);

      result.Assignments.Should().BeEmpty();
      result.Unattributed.Should().ContainSingle().Which.Identifier.Should().Be(202601010000);
   }

   [Fact]
   public void Match_WithAmbiguousIdentifierAcrossScopes_LeavesRowUnattributed()
   {
      // Two discovered migrations in different scopes share the identifier: attribution would be a guess.
      var executed = new[] { Executed(202601010000, "A1", scope: null) };
      var discovered = new IDbMigration[]
      {
         Migration(202601010000, "A1", "ScopeA"),
         Migration(202601010000, "B1", "ScopeB")
      };

      var result = ScopeBackfillMatcher.Match(executed, discovered);

      result.Assignments.Should().BeEmpty();
      result.Unattributed.Should().ContainSingle().Which.Identifier.Should().Be(202601010000);
   }

   [Fact]
   public void Match_WithSequentialRunners_EachFillsOnlyRecognizedRows()
   {
      // Runner 1 only knows ScopeA migrations; runner 2 only knows ScopeB migrations.
      var executed = new[]
      {
         Executed(202601010000, "A1", scope: null),
         Executed(202602010000, "B1", scope: null)
      };

      var runnerOne = ScopeBackfillMatcher.Match(executed, [Migration(202601010000, "A1", "ScopeA")]);

      runnerOne.Assignments.Should().ContainSingle().Which.Should().Be(new ScopeAssignment(202601010000, "ScopeA"));
      runnerOne.Unattributed.Should().ContainSingle().Which.Identifier.Should().Be(202602010000);

      // After runner 1 applied its assignment, only the ScopeB row is still scope-less.
      var executedAfterRunnerOne = new[]
      {
         Executed(202601010000, "A1", "ScopeA"),
         Executed(202602010000, "B1", scope: null)
      };

      var runnerTwo = ScopeBackfillMatcher.Match(executedAfterRunnerOne, [Migration(202602010000, "B1", "ScopeB")]);

      runnerTwo.Assignments.Should().ContainSingle().Which.Should().Be(new ScopeAssignment(202602010000, "ScopeB"));
      runnerTwo.Unattributed.Should().BeEmpty();
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
#pragma warning restore CS0618
