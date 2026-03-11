using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Scaffolding;

namespace mvdmio.Database.PgSQL.Tool.Pull;

/// <summary>
///    Writes generated table definition files into the configured project.
/// </summary>
internal class TableDefinitionWriter
{
   private readonly IPullFileSystem _fileSystem;

   public TableDefinitionWriter()
      : this(new PullFileSystem())
   {
   }

   internal TableDefinitionWriter(IPullFileSystem fileSystem)
   {
      _fileSystem = fileSystem;
   }

   public virtual async Task<TableDefinitionWriteResult> WriteAsync(
      ToolConfiguration config,
      IReadOnlyList<TableInfo> tables,
      IReadOnlyList<ConstraintInfo> constraints,
      CancellationToken cancellationToken = default
   )
   {
      var tablesDirectory = Path.Combine(ToolPathResolver.GetProjectDirectoryPath(config), "Tables");
      _fileSystem.CreateDirectory(tablesDirectory);

      var tableNamespace = NamespaceResolver.Resolve(tablesDirectory);
      var tableDefinitions = TableDefinitionScaffolder.Generate(tableNamespace, tables, constraints);

      foreach (var file in tableDefinitions.Files)
      {
         var filePath = Path.Combine(tablesDirectory, file.FileName);
         await _fileSystem.WriteAllTextAsync(filePath, file.Content, cancellationToken);
      }

      return new TableDefinitionWriteResult(tablesDirectory, tableDefinitions.Files.Count, tableDefinitions.Warnings);
   }
}

internal sealed record TableDefinitionWriteResult(
   string TablesDirectory,
   int GeneratedFileCount,
   IReadOnlyList<string> Warnings
);
