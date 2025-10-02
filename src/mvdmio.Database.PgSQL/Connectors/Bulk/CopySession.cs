using mvdmio.Database.PgSQL.Models;
using Npgsql;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Connectors.Bulk;

public sealed class CopySession<T> : IAsyncDisposable
{
   private readonly DatabaseConnection _db;
   private readonly string _tableName;
   private readonly Dictionary<string, Func<T, DbValue>> _columnValueMapping;

   private NpgsqlBinaryImporter _writer = null!;

   public CopySession(DatabaseConnection db, string tableName, Dictionary<string, Func<T, DbValue>> columnValueMapping)
   {
      _db = db;
      _tableName = tableName;
      _columnValueMapping = columnValueMapping;
   }

   internal async Task StartAsync(CancellationToken ct = default)
   {
      var sql = $"COPY {_tableName} ({string.Join(", ", _columnValueMapping.Keys)}) FROM STDIN (FORMAT BINARY)";
      _writer = await _db.Connection.BeginBinaryImportAsync(sql, ct);
   }

   public async Task WriteAsync(T item, CancellationToken ct = default)
   {
      await _writer.StartRowAsync(ct);

      foreach (var columnValueMap in _columnValueMapping)
      {
         var valueFunc = columnValueMap.Value;

         var value = valueFunc.Invoke(item);
         if (value.Value is null)
            await _writer.WriteNullAsync(ct);
         else if (value.Type is NpgsqlDbType.Unknown)
            await _writer.WriteAsync(value.Value, ct);
         else
            await _writer.WriteAsync(value.Value, value.Type, ct);
      }
   }

   public async Task FlushAsync(CancellationToken ct = default)
   {
      await _writer.CompleteAsync(ct);
   }

   public async ValueTask DisposeAsync()
   {
      await _writer.DisposeAsync();
      await _db.CloseAsync();
   }
}
