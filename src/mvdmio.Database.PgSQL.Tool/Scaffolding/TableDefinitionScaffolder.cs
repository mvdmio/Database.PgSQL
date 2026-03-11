using mvdmio.Database.PgSQL.Connectors.Schema.Models;

namespace mvdmio.Database.PgSQL.Tool.Scaffolding;

/// <summary>
///    Generates table definition classes from database schema metadata.
/// </summary>
internal static class TableDefinitionScaffolder
{
   public static TableDefinitionScaffoldingResult Generate(
      string tableNamespace,
      IEnumerable<TableInfo> tables,
      IEnumerable<ConstraintInfo> constraints
   )
   {
      var orderedTables = tables
         .OrderBy(x => x.Schema, StringComparer.Ordinal)
         .ThenBy(x => x.Name, StringComparer.Ordinal)
         .ToArray();
      var constraintLookup = TableDefinitionConstraintAnalyzer.BuildLookup(constraints);
      var classNames = TableDefinitionNameResolver.BuildClassNames(orderedTables);

      var files = new List<ScaffoldedTableFile>();
      var warnings = new List<string>();

      foreach (var table in orderedTables)
      {
         var key = TableDefinitionNameResolver.GetTableKey(table.Schema, table.Name);
         var tableConstraints = constraintLookup.GetValueOrDefault(key) ?? new TableConstraintMetadata();
         var supportsRepositoryGeneration = tableConstraints.PrimaryKeyColumns.Count == 1;

         if (!supportsRepositoryGeneration)
            warnings.Add(BuildRepositoryWarning(table, tableConstraints));

         files.Add(new ScaffoldedTableFile(
            FileName: $"{classNames[key]}.cs",
            Content: TableDefinitionContentBuilder.Build(
               tableNamespace,
               classNames[key],
               table,
               tableConstraints,
               supportsRepositoryGeneration
            )
         ));
      }

      return new TableDefinitionScaffoldingResult(files, warnings);
   }

   private static string BuildRepositoryWarning(TableInfo table, TableConstraintMetadata constraints)
   {
      var reason = constraints.PrimaryKeyColumns.Count == 0
         ? "no primary key was found"
         : "composite primary keys are not supported";

      return $"Skipped repository-ready attributes for {table.Schema}.{table.Name}: {reason}.";
   }

   internal sealed record TableDefinitionScaffoldingResult(
      IReadOnlyList<ScaffoldedTableFile> Files,
      IReadOnlyList<string> Warnings
   );

   internal sealed record ScaffoldedTableFile(
      string FileName,
      string Content
   );
}
