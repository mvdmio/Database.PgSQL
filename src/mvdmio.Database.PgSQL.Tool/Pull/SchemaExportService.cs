using mvdmio.Database.PgSQL.Connectors.Schema;
using mvdmio.Database.PgSQL.Connectors.Schema.Models;

namespace mvdmio.Database.PgSQL.Tool.Pull;

/// <summary>
///    Exports schema metadata from a PostgreSQL database.
/// </summary>
internal class SchemaExportService
{
   private readonly ISchemaExportClientFactory _clientFactory;

   public SchemaExportService()
      : this(new SchemaExportClientFactory())
   {
   }

   internal SchemaExportService(ISchemaExportClientFactory clientFactory)
   {
      _clientFactory = clientFactory;
   }

   public virtual async Task<SchemaExportResult> ExportAsync(
      string connectionString,
      IReadOnlyCollection<string>? schemas = null,
      CancellationToken cancellationToken = default
   )
   {
      await using var client = _clientFactory.Create(connectionString, schemas);

      var availableSchemas = await client.GetExportableSchemasAsync(cancellationToken);
      ValidateSchemas(schemas, availableSchemas);

      var script = await client.GenerateSchemaScriptAsync(cancellationToken);
      var tables = await client.GetTablesAsync(cancellationToken);
      var constraints = await client.GetConstraintsAsync(cancellationToken);
      var warnings = BuildWarnings(schemas, tables, constraints);

      return new SchemaExportResult(script, tables, constraints, warnings);
   }

   private static void ValidateSchemas(IReadOnlyCollection<string>? requestedSchemas, IReadOnlyList<string> availableSchemas)
   {
      if (requestedSchemas is not { Count: > 0 })
         return;

      var availableSchemaSet = new HashSet<string>(availableSchemas, StringComparer.Ordinal);
      var missingSchemas = requestedSchemas.Where(schema => !availableSchemaSet.Contains(schema)).ToArray();

      if (missingSchemas.Length == 0)
         return;

      throw new InvalidOperationException($"Configured schemas were not found in the database: {string.Join(", ", missingSchemas)}.");
   }

   private static IReadOnlyList<string> BuildWarnings(
      IReadOnlyCollection<string>? schemas,
      IReadOnlyList<TableInfo> tables,
      IReadOnlyList<ConstraintInfo> constraints
   )
   {
      if (schemas is not { Count: > 0 })
         return [];

      var includedSchemas = new HashSet<string>(schemas, StringComparer.Ordinal);

      var typeWarnings = tables
         .SelectMany(table => table.Columns.Select(column => new { TableSchema = table.Schema, TableName = table.Name, ColumnName = column.Name, column.DataType }))
         .SelectMany(column => ExtractSchemaQualifiedTypeNames(column.DataType)
            .Where(schemaQualifiedType => !includedSchemas.Contains(schemaQualifiedType.Schema))
            .Select(schemaQualifiedType =>
               $"Column '{column.TableSchema}.{column.TableName}.{column.ColumnName}' uses excluded type '{schemaQualifiedType.Schema}.{schemaQualifiedType.Name}'. Applying the exported schema to an empty database may fail unless that type already exists."))
         .Distinct(StringComparer.Ordinal);

      var foreignKeyWarnings = constraints
         .Where(constraint => string.Equals(constraint.ConstraintType, "f", StringComparison.Ordinal))
         .Where(constraint => constraint.ReferencedSchema is not null && !includedSchemas.Contains(constraint.ReferencedSchema))
         .Select(constraint =>
             $"Foreign key '{constraint.Schema}.{constraint.TableName}.{constraint.ConstraintName}' references excluded table '{constraint.ReferencedSchema}.{constraint.ReferencedTableName}'. Applying the exported schema to an empty database may fail unless that dependency already exists.")
         .Distinct(StringComparer.Ordinal);

      return typeWarnings
         .Concat(foreignKeyWarnings)
         .Distinct(StringComparer.Ordinal)
         .ToArray();
   }

   private static IEnumerable<(string Schema, string Name)> ExtractSchemaQualifiedTypeNames(string dataType)
   {
      const string identifier = "[A-Za-z_][A-Za-z0-9_$]*";
      var matches = System.Text.RegularExpressions.Regex.Matches(dataType, $@"(?<![A-Za-z0-9_""'])({identifier})\.({identifier})");

      foreach (System.Text.RegularExpressions.Match match in matches)
      {
         if (match.Groups.Count < 3)
            continue;

         yield return (match.Groups[1].Value, match.Groups[2].Value);
      }
   }
}

internal sealed record SchemaExportResult(
   string Script,
   IReadOnlyList<TableInfo> Tables,
   IReadOnlyList<ConstraintInfo> Constraints,
   IReadOnlyList<string> Warnings
);

internal interface ISchemaExportClientFactory
{
   ISchemaExportClient Create(string connectionString, IReadOnlyCollection<string>? schemas);
}

internal interface ISchemaExportClient : IAsyncDisposable
{
   Task<IReadOnlyList<string>> GetExportableSchemasAsync(CancellationToken cancellationToken);
   Task<string> GenerateSchemaScriptAsync(CancellationToken cancellationToken);
   Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken cancellationToken);
   Task<IReadOnlyList<ConstraintInfo>> GetConstraintsAsync(CancellationToken cancellationToken);
}

internal sealed class SchemaExportClientFactory : ISchemaExportClientFactory
{
   public ISchemaExportClient Create(string connectionString, IReadOnlyCollection<string>? schemas)
   {
      return new SchemaExportClient(new DatabaseConnection(connectionString), schemas);
   }
}

internal sealed class SchemaExportClient : ISchemaExportClient
{
   private readonly DatabaseConnection _connection;
   private readonly SchemaExtractor _schemaExtractor;

   public SchemaExportClient(DatabaseConnection connection, IReadOnlyCollection<string>? schemas)
   {
      _connection = connection;
      _schemaExtractor = new SchemaExtractor(connection, schemas);
   }

   public async ValueTask DisposeAsync()
   {
      await _connection.DisposeAsync();
   }

   public async Task<string> GenerateSchemaScriptAsync(CancellationToken cancellationToken)
   {
      return await _schemaExtractor.GenerateSchemaScriptAsync(cancellationToken);
   }

   public async Task<IReadOnlyList<string>> GetExportableSchemasAsync(CancellationToken cancellationToken)
   {
      return (await _schemaExtractor.GetExportableSchemasAsync(cancellationToken)).ToArray();
   }

   public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken cancellationToken)
   {
      return (await _schemaExtractor.GetTablesAsync(cancellationToken)).ToArray();
   }

   public async Task<IReadOnlyList<ConstraintInfo>> GetConstraintsAsync(CancellationToken cancellationToken)
   {
      return (await _schemaExtractor.GetConstraintsAsync(cancellationToken)).ToArray();
   }
}
