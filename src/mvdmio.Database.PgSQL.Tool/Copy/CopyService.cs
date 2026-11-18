using mvdmio.Database.PgSQL.Connectors.Schema;
using mvdmio.Database.PgSQL.Connectors.Schema.Models;

namespace mvdmio.Database.PgSQL.Tool.Copy;

internal class CopyService
{
   private readonly ICopyDatabaseFactory _databaseFactory;

   public CopyService()
      : this(new CopyDatabaseFactory())
   {
   }

   internal CopyService(ICopyDatabaseFactory databaseFactory)
   {
      _databaseFactory = databaseFactory;
   }

   public virtual async Task<CopyResult> CopyAsync(
      string sourceConnectionString,
      string destinationConnectionString,
      IReadOnlyCollection<string>? schemas,
      IReadOnlyCollection<string>? excludeTables,
      ICopyReporter reporter,
      CancellationToken cancellationToken = default
   )
   {
      await using var source = _databaseFactory.Create(sourceConnectionString, schemas);
      await using var destination = _databaseFactory.Create(destinationConnectionString, schemas);

      var sourceTables = await source.GetTablesAsync(cancellationToken);
      var destTables = await destination.GetTablesAsync(cancellationToken);

      var excludeSet = new HashSet<string>(excludeTables ?? [], StringComparer.Ordinal);

      var orderedTables = sourceTables
         .OrderBy(t => t.Schema, StringComparer.Ordinal)
         .ThenBy(t => t.Name, StringComparer.Ordinal)
         .Where(t => !excludeSet.Contains($"{t.Schema}.{t.Name}"))
         .ToArray();

      ValidateSchemaParity(orderedTables, destTables);

      var report = new List<CopyTableResult>(orderedTables.Length);

      await source.OpenAsync(cancellationToken);
      await destination.OpenAsync(cancellationToken);

      try
      {
         await destination.SetSessionReplicationRoleAsync("replica", cancellationToken);

         try
         {
            var truncatable = orderedTables
               .Where(t => t.Columns.Any(c => !c.IsGeneratedStored && !string.Equals(c.IdentityGeneration, "ALWAYS", StringComparison.Ordinal)))
               .Select(t => (t.Schema, t.Name))
               .ToArray();

            if (truncatable.Length > 0)
               await destination.TruncateManyAsync(truncatable, cancellationToken);

            foreach (var table in orderedTables)
            {
               cancellationToken.ThrowIfCancellationRequested();

               var copyableColumns = table.Columns
                  .Where(c => !c.IsGeneratedStored)
                  .Where(c => !string.Equals(c.IdentityGeneration, "ALWAYS", StringComparison.Ordinal))
                  .Select(c => c.Name)
                  .ToArray();

               if (copyableColumns.Length == 0)
               {
                  reporter.WriteWarning($"Skipping {table.Schema}.{table.Name}: no copyable columns (all are generated or identity ALWAYS).");
                  report.Add(new CopyTableResult(table.Schema, table.Name, 0, 0, true));
                  continue;
               }

               reporter.WriteInfo($"Copying {table.Schema}.{table.Name}...");

               var bytes = await destination.CopyFromAsync(source, table.Schema, table.Name, copyableColumns, cancellationToken);
               var rowCount = await destination.CountRowsAsync(table.Schema, table.Name, cancellationToken);

               report.Add(new CopyTableResult(table.Schema, table.Name, rowCount, bytes, false));
               reporter.WriteInfo($"  {rowCount} row(s), {bytes} byte(s)");
            }

            await ResetOwnedSequencesAsync(destination, orderedTables, schemas, cancellationToken);
         }
         finally
         {
            await destination.SetSessionReplicationRoleAsync("origin", CancellationToken.None);
         }
      }
      finally
      {
         await destination.CloseAsync(CancellationToken.None);
         await source.CloseAsync(CancellationToken.None);
      }

      return new CopyResult(report);
   }

   private static void ValidateSchemaParity(IReadOnlyList<TableInfo> sourceTables, IReadOnlyList<TableInfo> destTables)
   {
      var destLookup = destTables.ToDictionary(t => $"{t.Schema}.{t.Name}", StringComparer.Ordinal);
      var errors = new List<string>();

      foreach (var sourceTable in sourceTables)
      {
         var key = $"{sourceTable.Schema}.{sourceTable.Name}";

         if (!destLookup.TryGetValue(key, out var destTable))
         {
            errors.Add($"Table '{key}' exists in source but not in destination.");
            continue;
         }

         var sourceColumns = sourceTable.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
         var destColumns = destTable.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

         var missingInDest = sourceColumns.Except(destColumns).ToArray();
         if (missingInDest.Length > 0)
            errors.Add($"Table '{key}' is missing columns in destination: {string.Join(", ", missingInDest)}.");
      }

      if (errors.Count > 0)
         throw new InvalidOperationException("Destination schema does not match source. Run migrations on destination first.\n" + string.Join("\n", errors));
   }

   private static async Task ResetOwnedSequencesAsync(ICopyDatabaseClient destination, IReadOnlyList<TableInfo> tables, IReadOnlyCollection<string>? schemas, CancellationToken cancellationToken)
   {
      foreach (var table in tables)
      {
         if (schemas is { Count: > 0 } && !schemas.Contains(table.Schema))
            continue;

         foreach (var col in table.Columns)
         {
            if (!col.IsIdentity)
               continue;

            await destination.ResetSerialSequenceAsync(table.Schema, table.Name, col.Name, cancellationToken);
         }
      }

      var sequences = await destination.GetSequencesAsync(cancellationToken);
      foreach (var seq in sequences)
      {
         if (seq.OwnedByTable is null || seq.OwnedByColumn is null)
            continue;

         if (schemas is { Count: > 0 } && !schemas.Contains(seq.Schema))
            continue;

         await destination.ResetSequenceToColumnMaxAsync(seq.Schema, seq.Name, seq.OwnedByTable, seq.OwnedByColumn, cancellationToken);
      }
   }
}

internal sealed record CopyResult(IReadOnlyList<CopyTableResult> Tables)
{
   public long TotalRows => Tables.Sum(t => t.RowCount);
   public long TotalBytes => Tables.Sum(t => t.Bytes);
}

internal sealed record CopyTableResult(string Schema, string Name, long RowCount, long Bytes, bool Skipped);

internal interface ICopyReporter
{
   void WriteInfo(string message);
   void WriteWarning(string message);
   void WriteError(string message);
}

internal sealed class ConsoleCopyReporter : ICopyReporter
{
   public void WriteInfo(string message) => Console.WriteLine(message);
   public void WriteWarning(string message) => Console.WriteLine($"Warning: {message}");
   public void WriteError(string message) => Console.Error.WriteLine(message);
}

internal interface ICopyDatabaseFactory
{
   ICopyDatabaseClient Create(string connectionString, IReadOnlyCollection<string>? schemas);
}

internal interface ICopyDatabaseClient : IAsyncDisposable
{
   Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken cancellationToken);
   Task<IReadOnlyList<SequenceInfo>> GetSequencesAsync(CancellationToken cancellationToken);
   Task TruncateAsync(string schema, string table, CancellationToken cancellationToken);
   Task TruncateManyAsync(IReadOnlyList<(string Schema, string Name)> tables, CancellationToken cancellationToken);
   Task<long> CopyFromAsync(ICopyDatabaseClient source, string schema, string table, IReadOnlyList<string> columns, CancellationToken cancellationToken);
   Task<long> CountRowsAsync(string schema, string table, CancellationToken cancellationToken);
   Task SetSessionReplicationRoleAsync(string role, CancellationToken cancellationToken);
   Task ResetSequenceToColumnMaxAsync(string sequenceSchema, string sequenceName, string tableName, string columnName, CancellationToken cancellationToken);
   Task ResetSerialSequenceAsync(string tableSchema, string tableName, string columnName, CancellationToken cancellationToken);
   Task OpenAsync(CancellationToken cancellationToken);
   Task CloseAsync(CancellationToken cancellationToken);
   DatabaseConnection RawConnection { get; }
}

internal sealed class CopyDatabaseFactory : ICopyDatabaseFactory
{
   public ICopyDatabaseClient Create(string connectionString, IReadOnlyCollection<string>? schemas)
   {
      return new CopyDatabaseClient(new DatabaseConnection(connectionString), schemas);
   }
}

internal sealed class CopyDatabaseClient : ICopyDatabaseClient
{
   private readonly DatabaseConnection _connection;
   private readonly SchemaExtractor _schemaExtractor;

   public CopyDatabaseClient(DatabaseConnection connection, IReadOnlyCollection<string>? schemas)
   {
      _connection = connection;
      _schemaExtractor = new SchemaExtractor(connection, schemas);
   }

   public DatabaseConnection RawConnection => _connection;

   public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

   public async Task OpenAsync(CancellationToken cancellationToken) => await _connection.OpenAsync(cancellationToken);
   public Task CloseAsync(CancellationToken cancellationToken) => _connection.CloseAsync(cancellationToken);

   public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken cancellationToken)
      => (await _schemaExtractor.GetTablesAsync(cancellationToken)).ToArray();

   public async Task<IReadOnlyList<SequenceInfo>> GetSequencesAsync(CancellationToken cancellationToken)
      => (await _schemaExtractor.GetSequencesAsync(cancellationToken)).ToArray();

   public Task TruncateAsync(string schema, string table, CancellationToken cancellationToken)
   {
      return _connection.Dapper.ExecuteAsync($"TRUNCATE TABLE \"{schema}\".\"{table}\" RESTART IDENTITY CASCADE", ct: cancellationToken);
   }

   public Task TruncateManyAsync(IReadOnlyList<(string Schema, string Name)> tables, CancellationToken cancellationToken)
   {
      if (tables.Count == 0)
         return Task.CompletedTask;

      var qualified = string.Join(", ", tables.Select(t => $"\"{t.Schema}\".\"{t.Name}\""));
      return _connection.Dapper.ExecuteAsync($"TRUNCATE TABLE {qualified} RESTART IDENTITY CASCADE", ct: cancellationToken);
   }

   public Task<long> CopyFromAsync(ICopyDatabaseClient source, string schema, string table, IReadOnlyList<string> columns, CancellationToken cancellationToken)
   {
      return _connection.Bulk.CopyFromAsync(source.RawConnection, schema, table, columns, cancellationToken);
   }

   public Task<long> CountRowsAsync(string schema, string table, CancellationToken cancellationToken)
   {
      return _connection.Dapper.QuerySingleAsync<long>($"SELECT count(*) FROM \"{schema}\".\"{table}\"", ct: cancellationToken);
   }

   public Task SetSessionReplicationRoleAsync(string role, CancellationToken cancellationToken)
   {
      var validated = role switch
      {
         "origin" => "origin",
         "replica" => "replica",
         "local" => "local",
         _ => throw new ArgumentException($"Invalid session_replication_role value: {role}", nameof(role))
      };
      return _connection.Dapper.ExecuteAsync($"SET session_replication_role = {validated}", ct: cancellationToken);
   }

   public Task ResetSequenceToColumnMaxAsync(string sequenceSchema, string sequenceName, string tableName, string columnName, CancellationToken cancellationToken)
   {
      var sql = $"""
         SELECT setval('"{sequenceSchema}"."{sequenceName}"',
                       COALESCE((SELECT MAX("{columnName}") FROM "{sequenceSchema}"."{tableName}"), 1),
                       (SELECT COUNT(*) > 0 FROM "{sequenceSchema}"."{tableName}"))
         """;
      return _connection.Dapper.ExecuteAsync(sql, ct: cancellationToken);
   }

   public Task ResetSerialSequenceAsync(string tableSchema, string tableName, string columnName, CancellationToken cancellationToken)
   {
      var qualified = $"\"{tableSchema}\".\"{tableName}\"";
      var sql = $"""
         SELECT setval(seq, mx, has_rows)
         FROM (
            SELECT pg_get_serial_sequence('{qualified}', '{columnName}') AS seq,
                   COALESCE((SELECT MAX("{columnName}") FROM {qualified}), 1) AS mx,
                   (SELECT COUNT(*) > 0 FROM {qualified}) AS has_rows
         ) t
         WHERE t.seq IS NOT NULL
         """;
      return _connection.Dapper.ExecuteAsync(sql, ct: cancellationToken);
   }
}
