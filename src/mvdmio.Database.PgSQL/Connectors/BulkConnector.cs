using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Models;
using Npgsql;

namespace mvdmio.Database.PgSQL.Connectors;

/// <summary>
///    Connector for bulk-copying data to the database.
/// </summary>
[PublicAPI]
public class BulkConnector
{
   private readonly DatabaseConnection _db;
   
   /// <summary>
   ///    Constructor.
   /// </summary>
   public BulkConnector(DatabaseConnection db)
   {
      _db = db;
   }

   /// <summary>
   ///    Perform a binary copy to a given table.
   /// </summary>
   public async Task<CopyResult> CopyAsync<T>(string tableName, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, bool ignoreErrors = false, CancellationToken ct = default)
   {
      var rowsProvided = 0;
      var rowsWritten = 0;
      var errors = new List<Exception>();

      var sql = $"COPY {tableName} ({string.Join(", ", columnValueMapping.Keys)}) FROM STDIN (FORMAT BINARY)";
      await _db.OpenConnectionAndExecuteAsync(
         sql,
         async connection => {
            await using var writer = await connection.BeginBinaryImportAsync(sql, ct);

            foreach (var item in items)
            {
               try
               {
                  rowsProvided++;

                  await writer.StartRowAsync(ct);

                  foreach (var columnValueMap in columnValueMapping)
                  {
                     var valueFunc = columnValueMap.Value;

                     var value = valueFunc.Invoke(item);
                     if (value.Value is null)
                        await writer.WriteNullAsync(ct);
                     else
                        await writer.WriteAsync(value.Value, value.Type, ct);
                  }

                  rowsWritten++;
               }
               catch (PostgresException ex)
               {
                  errors.Add(ex);
                  
                  if (!ignoreErrors)
                     throw;
               }
            }

            try
            {
               await writer.CompleteAsync(ct);
            }
            catch (Exception ex)
            {
               errors.Add(ex);
               
               if(!ignoreErrors)
                  throw;
            }
         },
         ct
      );

      return new CopyResult {
         RowsProvided = rowsProvided,
         RowsWritten = rowsWritten,
         Errors = errors
      };
   }

   /// <summary>
   ///   Do an upsert on a table. Creates a temp-table, copies the data to it and then does an upsert on the original table.
   /// </summary>
   /// <param name="tableName">The table to do the upsert on</param>
   /// <param name="onConflictColumn">The on-conflict column</param>
   /// <param name="items">The data to upsert</param>
   /// <param name="columnValueMapping">The data-to-column mapping</param>
   /// <param name="ignoreErrors">Flag for ignoring errors. Will not throw exceptions when enabled</param>
   /// <param name="ct">The cancellation token</param>
   /// <typeparam name="T">The Type of the data to insert</typeparam>
   /// <returns>A task for asynchronous awaiting.</returns>
   public Task UpsertAsync<T>(string tableName, string onConflictColumn, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, bool ignoreErrors = false, CancellationToken ct = default)
   {
      return UpsertAsync(tableName, [ onConflictColumn ], items, columnValueMapping, ignoreErrors, ct);
   }

   /// <summary>
   ///   Do an upsert on a table. Creates a temp-table, copies the data to it and then does an upsert on the original table.
   /// </summary>
   /// <param name="tableName">The table to do the upsert on</param>
   /// <param name="onConflictColumns">The on-conflict columns</param>
   /// <param name="items">The data to upsert</param>
   /// <param name="columnValueMapping">The data-to-column mapping</param>
   /// <param name="ignoreErrors">Flag for ignoring errors. Will not throw exceptions when enabled</param>
   /// <param name="ct">The cancellation token</param>
   /// <typeparam name="T">The Type of the data to insert</typeparam>
   /// <returns>A task for asynchronous awaiting.</returns>
   public Task UpsertAsync<T>(string tableName, string[] onConflictColumns, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, bool ignoreErrors = false, CancellationToken ct = default)
   {
      return UpsertAsync(
         tableName,
         new UpsertConfiguration {
            OnConflictColumns = onConflictColumns
         },
         items,
         columnValueMapping,
         ignoreErrors,
         ct
      );
   }

   /// <summary>
   ///   Do an upsert on a table. Creates a temp-table, copies the data to it and then does an upsert on the original table.
   /// </summary>
   /// <param name="tableName">The table to do the upsert on</param>
   /// <param name="upsertConfiguration">The upsert configuration to use</param>
   /// <param name="items">The data to upsert</param>
   /// <param name="columnValueMapping">The data-to-column mapping</param>
   /// <param name="ignoreErrors">Flag for ignoring errors. Will not throw exceptions when enabled</param>
   /// <param name="ct">The cancellation token</param>
   /// <typeparam name="T">The Type of the data to insert</typeparam>
   /// <returns>A task for asynchronous awaiting.</returns>
   [SuppressMessage("ReSharper", "PossibleMultipleEnumeration", Justification = "Multiple enumerations are not a problem here.")]
   public async Task UpsertAsync<T>(string tableName, UpsertConfiguration upsertConfiguration, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, bool ignoreErrors = false, CancellationToken ct = default)
   {
      if (!items.Any())
         return;

      var tempTableName = $"temp_{Guid.NewGuid():N}";
      var allColumns = columnValueMapping.Keys;
      var updateColumns = allColumns.Where(x => !upsertConfiguration.OnConflictColumns.Contains(x)).ToArray();

      await _db.InTransactionAsync(
         async () => {
            await _db.Dapper.ExecuteAsync($"CREATE TEMP TABLE {tempTableName} (LIKE {tableName} INCLUDING CONSTRAINTS INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY);");

            await CopyAsync(tempTableName, items, columnValueMapping, ignoreErrors, ct);

            var onConflictWhereClause = string.IsNullOrEmpty(upsertConfiguration.OnConflictWhereClause)
               ? string.Empty
               : $"WHERE {upsertConfiguration.OnConflictWhereClause}";
            
            await _db.Dapper.ExecuteAsync(
               $"""
                INSERT INTO {tableName} ({string.Join(", ", allColumns)})
                SELECT {string.Join(", ", allColumns)}
                FROM {tempTableName}
                ON CONFLICT ({string.Join(", ", upsertConfiguration.OnConflictColumns)}) {onConflictWhereClause } DO UPDATE
                SET {string.Join(", ", updateColumns.Select(x => $"{x} = EXCLUDED.{x}"))}
                """
            );
         }
      );
   }
}