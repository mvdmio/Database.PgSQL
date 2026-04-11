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

   public virtual async Task<SchemaExportResult> ExportAsync(string connectionString, CancellationToken cancellationToken = default)
   {
      await using var client = _clientFactory.Create(connectionString);

      var script = await client.GenerateSchemaScriptAsync(cancellationToken);
      var tables = await client.GetTablesAsync(cancellationToken);
      var constraints = await client.GetConstraintsAsync(cancellationToken);

      return new SchemaExportResult(script, tables, constraints);
   }
}

internal sealed record SchemaExportResult(
   string Script,
   IReadOnlyList<TableInfo> Tables,
   IReadOnlyList<ConstraintInfo> Constraints
);

internal interface ISchemaExportClientFactory
{
   ISchemaExportClient Create(string connectionString);
}

internal interface ISchemaExportClient : IAsyncDisposable
{
   Task<string> GenerateSchemaScriptAsync(CancellationToken cancellationToken);
   Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken cancellationToken);
   Task<IReadOnlyList<ConstraintInfo>> GetConstraintsAsync(CancellationToken cancellationToken);
}

internal sealed class SchemaExportClientFactory : ISchemaExportClientFactory
{
   public ISchemaExportClient Create(string connectionString)
   {
      return new SchemaExportClient(new DatabaseConnection(connectionString));
   }
}

internal sealed class SchemaExportClient : ISchemaExportClient
{
   private readonly DatabaseConnection _connection;
   private readonly SchemaExtractor _schemaExtractor;

   public SchemaExportClient(DatabaseConnection connection)
   {
      _connection = connection;
      _schemaExtractor = new SchemaExtractor(connection);
   }

   public async ValueTask DisposeAsync()
   {
      await _connection.DisposeAsync();
   }

   public async Task<string> GenerateSchemaScriptAsync(CancellationToken cancellationToken)
   {
      return await _schemaExtractor.GenerateSchemaScriptAsync(cancellationToken);
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
