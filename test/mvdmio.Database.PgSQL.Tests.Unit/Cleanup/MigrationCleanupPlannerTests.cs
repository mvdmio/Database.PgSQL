using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using mvdmio.Database.PgSQL.Tool.Cleanup;

namespace mvdmio.Database.PgSQL.Tests.Unit.Cleanup;

public class MigrationCleanupPlannerTests : IDisposable
{
   private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"cleanup-tests-{Guid.NewGuid():N}");

   public MigrationCleanupPlannerTests()
   {
      Directory.CreateDirectory(_tempDirectory);
   }

   [Fact]
   public void Plan_WithNoEnvironments_ReturnsSkipReason()
   {
      var plan = MigrationCleanupPlanner.Plan(_tempDirectory, [], []);

      plan.LowestMigrationIdentifier.Should().BeNull();
      plan.FilesToDelete.Should().BeEmpty();
      plan.PendingFilesKept.Should().BeEmpty();
      plan.SkipReason.Should().Be("No environments configured.");
   }

   [Fact]
   public void Plan_WithEnvironmentWithoutMigrationVersion_ReturnsSkipReason()
   {
      var plan = MigrationCleanupPlanner.Plan(_tempDirectory, [], [Env("dev", 202602161430), Env("prod", null)]);

      plan.LowestMigrationIdentifier.Should().BeNull();
      plan.FilesToDelete.Should().BeEmpty();
      plan.PendingFilesKept.Should().BeEmpty();
      plan.SkipReason.Should().Be("At least one environment has no recorded migration version.");
   }

   [Fact]
   public void Plan_WithMissingMigrationsDirectory_ReturnsLowestVersionWithoutFiles()
   {
      var missingDirectory = Path.Combine(_tempDirectory, "missing");

      var plan = MigrationCleanupPlanner.Plan(missingDirectory, [], [Env("dev", 202602161430), Env("prod", 202602171500)]);

      plan.LowestMigrationIdentifier.Should().Be(202602161430);
      plan.FilesToDelete.Should().BeEmpty();
      plan.PendingFilesKept.Should().BeEmpty();
      plan.SkipReason.Should().BeNull();
   }

   [Fact]
   public void Plan_WithMigrationFilesOlderThanLowestVersion_ReturnsOnlyObsoleteFiles()
   {
      var migrationsDirectory = Path.Combine(_tempDirectory, "Migrations");
      var nestedDirectory = Path.Combine(migrationsDirectory, "Nested");
      Directory.CreateDirectory(nestedDirectory);

      var obsoleteRoot = CreateFile(migrationsDirectory, "_202602151200_CreateUsers.cs");
      var obsoleteNested = CreateFile(nestedDirectory, "_202602161429_CreateOrders.cs");
      var current = CreateFile(migrationsDirectory, "_202602161430_CreateProducts.cs");
      var newer = CreateFile(migrationsDirectory, "_202602171500_CreateInvoices.cs");
      _ = CreateFile(migrationsDirectory, "Helpers.cs");

      var plan = MigrationCleanupPlanner.Plan(migrationsDirectory, [], [Env("dev", 202602171500), Env("test", 202602161430), Env("prod", 202602191200)]);

      plan.LowestMigrationIdentifier.Should().Be(202602161430);
      plan.FilesToDelete.Should().BeEquivalentTo(obsoleteNested, obsoleteRoot);
      plan.FilesToDelete.Should().NotContain(current);
      plan.FilesToDelete.Should().NotContain(newer);
      plan.PendingFilesKept.Should().BeEmpty();
      plan.SkipReason.Should().BeNull();
   }

   [Fact]
   public void Plan_WithMigrationPendingInOneEnvironment_KeepsItAndDeletesFileEveryEnvironmentHas()
   {
      var everywhere = CreateFile(_tempDirectory, "_202602151200_CreateUsers.cs");
      var outOfOrderInProd = CreateFile(_tempDirectory, "_202602161000_CreateOrders.cs");
      IDbMigration[] discovered = [Migration(202602151200, "ScopeA"), Migration(202602161000, "ScopeA"), Migration(202602170000, "ScopeA")];

      // dev ran everything; prod ran the later migration first, so 202602161000 is out-of-order there.
      var dev = Env("dev", 202602170000, Executed(202602151200, "ScopeA"), Executed(202602161000, "ScopeA"), Executed(202602170000, "ScopeA"));
      var prod = Env("prod", 202602170000, Executed(202602151200, "ScopeA"), Executed(202602170000, "ScopeA"));

      var plan = MigrationCleanupPlanner.Plan(_tempDirectory, discovered, [dev, prod]);

      plan.LowestMigrationIdentifier.Should().Be(202602170000);
      plan.FilesToDelete.Should().Equal(everywhere);
      plan.PendingFilesKept.Should().ContainSingle();
      plan.PendingFilesKept[0].Path.Should().Be(outOfOrderInProd);
      plan.PendingFilesKept[0].PendingEnvironments.Should().Equal("prod");
   }

   [Fact]
   public void Plan_WithIdentifierSharedByTwoScopes_KeepsFileWhenEitherMigrationIsPending()
   {
      var shared = CreateFile(_tempDirectory, "_202602161000_Shared.cs");
      IDbMigration[] discovered =
      [
         Migration(202602150000, "ScopeA"),
         Migration(202602161000, "ScopeA"),
         Migration(202602170000, "ScopeA"),
         Migration(202602150000, "ScopeB"),
         Migration(202602161000, "ScopeB"),
         Migration(202602170000, "ScopeB")
      ];

      // ScopeA has the shared identifier everywhere; ScopeB's migration with that identifier is out-of-order in prod.
      var dev = Env("dev", 202602170000, ExecutedInBothScopes(202602150000, 202602161000, 202602170000));
      var prod = Env(
         "prod",
         202602170000,
         Executed(202602150000, "ScopeA"),
         Executed(202602161000, "ScopeA"),
         Executed(202602170000, "ScopeA"),
         Executed(202602150000, "ScopeB"),
         Executed(202602170000, "ScopeB")
      );

      var plan = MigrationCleanupPlanner.Plan(_tempDirectory, discovered, [dev, prod]);

      plan.FilesToDelete.Should().BeEmpty();
      plan.PendingFilesKept.Should().ContainSingle();
      plan.PendingFilesKept[0].Path.Should().Be(shared);
      plan.PendingFilesKept[0].PendingEnvironments.Should().Equal("prod");
   }

   [Fact]
   public void Plan_WithEnvironmentWithoutRows_KeepsEveryFileMatchingADiscoveredMigration()
   {
      var first = CreateFile(_tempDirectory, "_202602151200_CreateUsers.cs");
      var second = CreateFile(_tempDirectory, "_202602161000_CreateOrders.cs");
      var unmatched = CreateFile(_tempDirectory, "_202602140000_Removed.cs");
      IDbMigration[] discovered = [Migration(202602151200, "ScopeA"), Migration(202602161000, "ScopeA")];

      var dev = Env("dev", 202602170000, Executed(202602151200, "ScopeA"), Executed(202602161000, "ScopeA"), Executed(202602170000, "ScopeA"));
      var fresh = Env("fresh", 202602170000);

      var plan = MigrationCleanupPlanner.Plan(_tempDirectory, discovered, [dev, fresh]);

      plan.FilesToDelete.Should().Equal(unmatched);
      plan.PendingFilesKept.Select(x => x.Path).Should().Equal(first, second);
      plan.PendingFilesKept.Should().AllSatisfy(x => x.PendingEnvironments.Should().Equal("fresh"));
   }

   [Theory]
   [InlineData("_202602161430_AddUsers", true, 202602161430L)]
   [InlineData("202602161430_AddUsers", true, 202602161430L)]
   [InlineData("Helpers", false, 0L)]
   public void TryParseMigrationIdentifier_ReturnsExpectedResult(string fileNameWithoutExtension, bool expectedResult, long expectedIdentifier)
   {
      var result = MigrationCleanupPlanner.TryParseMigrationIdentifier(fileNameWithoutExtension, out var identifier);

      result.Should().Be(expectedResult);
      identifier.Should().Be(expectedIdentifier);
   }

   public void Dispose()
   {
      if (Directory.Exists(_tempDirectory))
         Directory.Delete(_tempDirectory, recursive: true);
   }

   private static CleanupEnvironment Env(string name, long? lowestHeaderIdentifier, params ExecutedMigrationModel[] executedMigrations)
   {
      return new CleanupEnvironment(name, lowestHeaderIdentifier, executedMigrations);
   }

   private static ExecutedMigrationModel[] ExecutedInBothScopes(params long[] identifiers)
   {
      return identifiers.SelectMany(identifier => new[] { Executed(identifier, "ScopeA"), Executed(identifier, "ScopeB") }).ToArray();
   }

   private static ExecutedMigrationModel Executed(long identifier, string scope)
   {
      return new ExecutedMigrationModel(identifier, $"Migration{identifier}", DateTime.UtcNow, scope);
   }

   private static IDbMigration Migration(long identifier, string scope)
   {
      return new FakeMigration(identifier, scope);
   }

   private static string CreateFile(string directory, string fileName)
   {
      var path = Path.Combine(directory, fileName);
      File.WriteAllText(path, "// test");
      return path;
   }

   private sealed class FakeMigration(long identifier, string scope) : IDbMigration
   {
      public long Identifier => identifier;
      public string Name => $"Migration{identifier}";
      public string Scope => scope;

      public Task UpAsync(DatabaseConnection db)
      {
         return Task.CompletedTask;
      }
   }
}
