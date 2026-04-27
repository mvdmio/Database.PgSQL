using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Bulk;

/// <summary>
///    Cross-connection streaming COPY operations.
/// </summary>
public sealed partial class BulkConnector
{
   /// <summary>
   ///    Stream all rows from a table on the <paramref name="source"/> connection into the same-named table on this connection
   ///    using PostgreSQL binary COPY. Both connections must already point at databases where the table exists with the same
   ///    column set in the same order. The destination table is not truncated; that is the caller's responsibility.
   /// </summary>
   /// <param name="source">The source database connection.</param>
   /// <param name="schema">The schema name. Must be a valid unquoted PostgreSQL identifier.</param>
   /// <param name="table">The table name. Must be a valid unquoted PostgreSQL identifier.</param>
   /// <param name="columns">The columns to copy. Must exist in both source and destination, in the same order. Each must be a valid unquoted PostgreSQL identifier.</param>
   /// <param name="ct">A cancellation token.</param>
   /// <returns>The number of bytes streamed from the source to the destination.</returns>
   [PublicAPI]
   public async Task<long> CopyFromAsync(DatabaseConnection source, string schema, string table, IReadOnlyList<string> columns, CancellationToken ct = default)
   {
      ArgumentNullException.ThrowIfNull(source);
      ArgumentNullException.ThrowIfNull(columns);
      ValidateIdentifier(schema, nameof(schema));
      ValidateIdentifier(table, nameof(table));

      if (columns.Count == 0)
         throw new ArgumentException("At least one column must be specified.", nameof(columns));

      foreach (var column in columns)
         ValidateIdentifier(column, nameof(columns));

      var qualifiedTable = $"\"{schema}\".\"{table}\"";
      var quotedColumns = string.Join(", ", columns.Select(c => $"\"{c}\""));
      var copyOutSql = $"COPY {qualifiedTable} ({quotedColumns}) TO STDOUT (FORMAT BINARY)";
      var copyInSql = $"COPY {qualifiedTable} ({quotedColumns}) FROM STDIN (FORMAT BINARY)";

      var sourceOpened = await source.OpenAsync(ct);
      var destOpened = await _db.OpenAsync(ct);

      try
      {
         await using var reader = await source.Connection!.BeginRawBinaryCopyAsync(copyOutSql, ct);
         await using var writer = await _db.Connection!.BeginRawBinaryCopyAsync(copyInSql, ct);

         var bytesCopied = await CopyStreamAsync(reader, writer, ct);
         return bytesCopied;
      }
      finally
      {
         if (destOpened)
            await _db.CloseAsync(ct);

         if (sourceOpened)
            await source.CloseAsync(ct);
      }
   }

   private static async Task<long> CopyStreamAsync(Stream source, Stream destination, CancellationToken ct)
   {
      const int bufferSize = 81920;
      var buffer = new byte[bufferSize];
      long total = 0;
      int read;

      while ((read = await source.ReadAsync(buffer.AsMemory(0, bufferSize), ct).ConfigureAwait(false)) > 0)
      {
         await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
         total += read;
      }

      return total;
   }
}
