using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.Models;
using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Tool.Cleanup;

/// <summary>
///    What cleanup knows about one configured environment.
/// </summary>
/// <param name="Name">The environment name from the configuration.</param>
/// <param name="LowestHeaderIdentifier">The lowest migration version line in the environment's pulled schema header, or <see langword="null" /> when it has none.</param>
/// <param name="ExecutedMigrations">The rows recorded in the environment's migrations table; empty when it has none.</param>
internal sealed record CleanupEnvironment(string Name, long? LowestHeaderIdentifier, IReadOnlyCollection<ExecutedMigrationModel> ExecutedMigrations);

/// <summary>
///    A migration file below the deletion bound that cleanup keeps because its migration is still pending.
/// </summary>
/// <param name="Path">The migration file.</param>
/// <param name="PendingEnvironments">The environments where a migration with the file's identifier is still pending.</param>
internal sealed record PendingMigrationFile(string Path, string[] PendingEnvironments);

internal sealed record MigrationCleanupPlan(long? LowestMigrationIdentifier, string[] FilesToDelete, PendingMigrationFile[] PendingFilesKept, string? SkipReason);

internal static partial class MigrationCleanupPlanner
{
   [GeneratedRegex(@"^_?(\d{12})_(.+)$")]
   private static partial Regex MigrationFileNameRegex();

   /// <summary>
   ///    Plans which migration files to delete: files below the lowest header line across environments, minus every
   ///    file whose identifier belongs to a discovered migration still pending in at least one environment. A file
   ///    name carries no scope, so a file matches every discovered migration with its identifier.
   /// </summary>
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

      var candidates = Directory
         .GetFiles(migrationsDirectoryPath, "*.cs", SearchOption.AllDirectories)
         .Select(path => (Path: path, IsMigration: TryParseMigrationIdentifier(Path.GetFileNameWithoutExtension(path), out var identifier), Identifier: identifier))
         .Where(x => x.IsMigration && x.Identifier < lowestMigrationIdentifier)
         .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase);

      foreach (var candidate in candidates)
      {
         var pendingEnvironments = pendingIdentifiersByEnvironment
            .Where(x => x.Identifiers.Contains(candidate.Identifier))
            .Select(x => x.Name)
            .ToArray();

         if (pendingEnvironments.Length == 0)
            filesToDelete.Add(candidate.Path);
         else
            pendingFilesKept.Add(new PendingMigrationFile(candidate.Path, pendingEnvironments));
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
