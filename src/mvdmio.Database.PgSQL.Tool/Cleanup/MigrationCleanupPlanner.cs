using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Tool.Cleanup;

internal sealed record MigrationCleanupPlan(long? LowestMigrationIdentifier, string[] FilesToDelete, string? SkipReason);

internal static partial class MigrationCleanupPlanner
{
   [GeneratedRegex(@"^_?(\d{12})_(.+)$")]
   private static partial Regex MigrationFileNameRegex();

   public static MigrationCleanupPlan Plan(string migrationsDirectoryPath, IReadOnlyCollection<long?> environmentMigrationIdentifiers)
   {
      if (environmentMigrationIdentifiers.Count == 0)
         return new(null, [], "No environments configured.");

      if (environmentMigrationIdentifiers.Any(x => x is null))
         return new(null, [], "At least one environment has no recorded migration version.");

      var lowestMigrationIdentifier = environmentMigrationIdentifiers.Min()!.Value;

      if (!Directory.Exists(migrationsDirectoryPath))
         return new(lowestMigrationIdentifier, [], null);

      var filesToDelete = Directory
         .GetFiles(migrationsDirectoryPath, "*.cs", SearchOption.AllDirectories)
         .Where(path => TryParseMigrationIdentifier(Path.GetFileNameWithoutExtension(path), out var identifier) && identifier < lowestMigrationIdentifier)
         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
         .ToArray();

      return new(lowestMigrationIdentifier, filesToDelete, null);
   }

   internal static bool TryParseMigrationIdentifier(string fileNameWithoutExtension, out long identifier)
   {
      var match = MigrationFileNameRegex().Match(fileNameWithoutExtension);

      if (!match.Success)
      {
         identifier = default;
         return false;
      }

      return long.TryParse(match.Groups[1].Value, out identifier);
   }
}
