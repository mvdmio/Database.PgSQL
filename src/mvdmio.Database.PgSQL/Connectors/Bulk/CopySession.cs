using mvdmio.Database.PgSQL.Models;
using Npgsql;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Connectors.Bulk;

/// <summary>
/// Represents a session for performing bulk copy operations to a PostgreSQL table using binary import.
/// </summary>
/// <typeparam name="T">The type of items to be written to the database.</typeparam>
public sealed class CopySession<T>
{
   private readonly DatabaseConnection _db;
   private readonly string _tableName;
   private readonly Dictionary<string, Func<T, DbValue>> _columnValueMapping;

   private bool _connectionOpened;
   private NpgsqlBinaryImporter _writer = null!;

   /// <summary>
   /// Initializes a new instance of the <see cref="CopySession{T}"/> class.
   /// </summary>
   /// <param name="db">The database connection to use for the copy operation.</param>
   /// <param name="tableName">The name of the target table for the copy operation.</param>
   /// <param name="columnValueMapping">A dictionary mapping column names to functions that extract values from items of type <typeparamref name="T"/>.</param>
   public CopySession(DatabaseConnection db, string tableName, Dictionary<string, Func<T, DbValue>> columnValueMapping)
   {
      _db = db;
      _tableName = tableName;
      _columnValueMapping = columnValueMapping;
   }

   internal async Task BeginAsync(CancellationToken ct = default)
   {
      var sql = $"COPY {_tableName} ({string.Join(", ", _columnValueMapping.Keys)}) FROM STDIN (FORMAT BINARY)";
      _connectionOpened = await _db.OpenAsync(ct);
      _writer = await _db.Connection!.BeginBinaryImportAsync(sql, ct);
   }

   /// <summary>
   /// Writes a single item to the database as a new row in the copy operation.
   /// </summary>
   /// <param name="item">The item to write to the database.</param>
   /// <param name="ct">A cancellation token to observe while waiting for the task to complete.</param>
   /// <returns>A task representing the asynchronous write operation.</returns>
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

   /// <summary>
   /// Completes the copy operation, committing all written rows to the database and closing the connection if it was opened by this session.
   /// </summary>
   /// <param name="ct">A cancellation token to observe while waiting for the task to complete.</param>
   /// <returns>A task representing the asynchronous complete operation.</returns>
   public async Task CompleteAsync(CancellationToken ct = default)
   {
      await _writer.CompleteAsync(ct);
      await _writer.DisposeAsync();

      if (_connectionOpened)
         await _db.CloseAsync(ct);
   }
}
