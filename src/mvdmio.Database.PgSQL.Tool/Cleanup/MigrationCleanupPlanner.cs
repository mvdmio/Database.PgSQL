using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Tool.Cleanup;

internal sealed record CleanupEnvironment(string Name, long? LowestHeaderIdentifier, IReadOnlyCollection<ExecutedMigrationModel> ExecutedMigrations);

internal sealed record PendingMigrationFile(string Path, string[] PendingEnvironments);

internal sealed record MigrationCleanupPlan(long? LowestMigrationIdentifier, string[] FilesToDelete, PendingMigrationFile[] PendingFilesKept, string? SkipReason);

internal static partial class MigrationCleanupPlanner
{
   [GeneratedRegex(@"^_?(\d{12})_(.+)$")]
   private static partial Regex MigrationFileNameRegex();

   public static MigrationCleanupPlan Plan(
      string migrationsDirectoryPath,
      IReadOnlyCollection<IDbMigration> discoveredMigrations,
      IReadOnlyCollection<CleanupEnvironment> environments
   )
   {
      if (environments.Count == 0)
         return new(null, [], [], "No environments configured.");

      if (environments.Any(x => x.LowestHeaderIdentifier is null))
         return new(null, [], [], "At least one environment has no recorded migration version.");

      var lowestMigrationIdentifier = environments.Min(x => x.LowestHeaderIdentifier)!.Value;

      if (!Directory.Exists(migrationsDirectoryPath))
         return new(lowestMigrationIdentifier, [], [], null);

      var pendingIdentifiersByEnvironment = environments
         .Select(environment => (
            environment.Name,
            Identifiers: PendingMigrationSelector
               .SelectPending(environment.ExecutedMigrations, discoveredMigrations)
               .Select(x => x.Migration.Identifier)
               .ToHashSet()
         ))
         .ToArray();

      var filesToDelete = new List<string>();
      var pendingFilesKept = new List<PendingMigrationFile>();

      var files = Directory
         .GetFiles(migrationsDirectoryPath, "*.cs", SearchOption.AllDirectories)
         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

      foreach (var path in files)
      {
         if (!TryParseMigrationIdentifier(Path.GetFileNameWithoutExtension(path), out var identifier) || identifier >= lowestMigrationIdentifier)
            continue;

         var pendingEnvironments = pendingIdentifiersByEnvironment
            .Where(x => x.Identifiers.Contains(identifier))
            .Select(x => x.Name)
            .ToArray();

         if (pendingEnvironments.Length == 0)
            filesToDelete.Add(path);
         else
            pendingFilesKept.Add(new PendingMigrationFile(path, pendingEnvironments));
      }

      return new(lowestMigrationIdentifier, filesToDelete.ToArray(), pendingFilesKept.ToArray(), null);
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
