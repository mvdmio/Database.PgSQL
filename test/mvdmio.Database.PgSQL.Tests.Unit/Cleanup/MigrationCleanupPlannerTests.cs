using AwesomeAssertions;
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
      var plan = MigrationCleanupPlanner.Plan(_tempDirectory, []);

      plan.LowestMigrationIdentifier.Should().BeNull();
      plan.FilesToDelete.Should().BeEmpty();
      plan.SkipReason.Should().Be("No environments configured.");
   }

   [Fact]
   public void Plan_WithEnvironmentWithoutMigrationVersion_ReturnsSkipReason()
   {
      var plan = MigrationCleanupPlanner.Plan(_tempDirectory, [202602161430, null]);

      plan.LowestMigrationIdentifier.Should().BeNull();
      plan.FilesToDelete.Should().BeEmpty();
      plan.SkipReason.Should().Be("At least one environment has no recorded migration version.");
   }

   [Fact]
   public void Plan_WithMissingMigrationsDirectory_ReturnsLowestVersionWithoutFiles()
   {
      var missingDirectory = Path.Combine(_tempDirectory, "missing");

      var plan = MigrationCleanupPlanner.Plan(missingDirectory, [202602161430, 202602171500]);

      plan.LowestMigrationIdentifier.Should().Be(202602161430);
      plan.FilesToDelete.Should().BeEmpty();
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

      var plan = MigrationCleanupPlanner.Plan(migrationsDirectory, [202602171500, 202602161430, 202602191200]);

      plan.LowestMigrationIdentifier.Should().Be(202602161430);
      plan.FilesToDelete.Should().BeEquivalentTo(obsoleteNested, obsoleteRoot);
      plan.FilesToDelete.Should().NotContain(current);
      plan.FilesToDelete.Should().NotContain(newer);
      plan.SkipReason.Should().BeNull();
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

   private static string CreateFile(string directory, string fileName)
   {
      var path = Path.Combine(directory, fileName);
      File.WriteAllText(path, "// test");
      return path;
   }
}
