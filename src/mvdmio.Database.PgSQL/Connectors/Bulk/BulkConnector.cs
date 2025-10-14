using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Models;
using Npgsql;
using System.Diagnostics.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Connectors.Bulk;

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
   ///   Begin a binary copy session.
   /// </summary>
   public async Task<CopySession<T>> BeginCopyAsync<T>(string tableName, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      var copySession = new CopySession<T>(_db, tableName, columnValueMapping);
      await copySession.BeginAsync(ct);

      return copySession;
   }

   /// <summary>
   ///    Perform a binary copy to a given table.
   /// </summary>
   [SuppressMessage("ReSharper",   "PossibleMultipleEnumeration",                                       Justification = "Multiple enumerations are not a problem here.")]
   [SuppressMessage("Performance", "CA1851:Possible multiple enumerations of 'IEnumerable' collection", Justification = "Multiple enumerations are not a problem here.")]
   public async Task CopyAsync<T>(string tableName, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      if (!items.Any())
         return;

      var errors = new List<Exception>();

      var copySession = await BeginCopyAsync(tableName, columnValueMapping, ct);

      foreach (var item in items)
      {
         try
         {
            await copySession.WriteAsync(item, ct);
         }
         catch (PostgresException ex)
         {
            errors.Add(ex);
         }
      }

      try
      {
         await copySession.CompleteAsync(ct);
      }
      catch (Exception ex)
      {
         errors.Add(ex);
      }

      if(errors.Count is not 0)
         throw new AggregateException("Errors occurred during bulk copy.", errors);
   }

   /// <summary>
   ///   Do an upsert on a table. Creates a temp-table, copies the data to it and then does an upsert on the original table.
   /// </summary>
   /// <param name="tableName">The table to do the upsert on</param>
   /// <param name="onConflictColumn">The on-conflict column</param>
   /// <param name="items">The data to upsert</param>
   /// <param name="columnValueMapping">The data-to-column mapping</param>
   /// <param name="ct">The cancellation token</param>
   /// <typeparam name="T">The Type of the data to insert</typeparam>
   /// <returns>A task for asynchronous awaiting.</returns>
   public Task InsertOrUpdateAsync<T>(string tableName, string onConflictColumn, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      return InsertOrUpdateAsync(tableName, [ onConflictColumn ], items, columnValueMapping, ct);
   }

   /// <summary>
   ///   Do an upsert on a table. Creates a temp-table, copies the data to it and then does an upsert on the original table.
   /// </summary>
   /// <param name="tableName">The table to do the upsert on</param>
   /// <param name="onConflictColumns">The on-conflict columns</param>
   /// <param name="items">The data to upsert</param>
   /// <param name="columnValueMapping">The data-to-column mapping</param>
   /// <param name="ct">The cancellation token</param>
   /// <typeparam name="T">The Type of the data to insert</typeparam>
   /// <returns>A task for asynchronous awaiting.</returns>
   public Task InsertOrUpdateAsync<T>(string tableName, string[] onConflictColumns, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      return InsertOrUpdateAsync(
         tableName,
         new UpsertConfiguration {
            OnConflictColumns = onConflictColumns
         },
         items,
         columnValueMapping,
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
   /// <param name="ct">The cancellation token</param>
   /// <typeparam name="T">The Type of the data to insert</typeparam>
   /// <returns>A task for asynchronous awaiting.</returns>
   [SuppressMessage("ReSharper", "PossibleMultipleEnumeration", Justification = "Multiple enumerations are not a problem here.")]
   public async Task InsertOrUpdateAsync<T>(string tableName, UpsertConfiguration upsertConfiguration, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      if (!items.Any())
         return;

      var tempTableName = $"temp_{Guid.NewGuid():N}";
      var allColumns = columnValueMapping.Keys;
      var updateColumns = allColumns.Where(x => !upsertConfiguration.OnConflictColumns.Contains(x)).ToArray();

      await _db.InTransactionAsync(
         async () => {
            await _db.Dapper.ExecuteAsync($"CREATE TEMP TABLE {tempTableName} (LIKE {tableName} INCLUDING CONSTRAINTS INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY);");

            await CopyAsync(tempTableName, items, columnValueMapping, ct);

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

   /// <summary>
   ///   Do an insert on a table. Creates a temp-table, copies the data to it and then does an insert on the original table. Rows that conflict with existing data are skipped.
   /// </summary>
   /// <param name="tableName">The table to do the insert on</param>
   /// <param name="onConflictColumn">The on-conflict column</param>
   /// <param name="items">The data to insert</param>
   /// <param name="columnValueMapping">The data-to-column mapping</param>
   /// <param name="ct">The cancellation token</param>
   /// <typeparam name="T">The Type of the data to insert</typeparam>
   /// <returns>A task for asynchronous awaiting.</returns>
   public Task InsertOrSkipAsync<T>(string tableName, string onConflictColumn, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      return InsertOrSkipAsync(tableName, [ onConflictColumn ], items, columnValueMapping, ct);
   }

   /// <summary>
   ///   Do an insert on a table. Creates a temp-table, copies the data to it and then does an insert on the original table. Rows that conflict with existing data are skipped.
   /// </summary>
   /// <param name="tableName">The table to do the insert on</param>
   /// <param name="onConflictColumns">The on-conflict columns</param>
   /// <param name="items">The data to insert</param>
   /// <param name="columnValueMapping">The data-to-column mapping</param>
   /// <param name="ct">The cancellation token</param>
   /// <typeparam name="T">The Type of the data to insert</typeparam>
   /// <returns>A task for asynchronous awaiting.</returns>
   public Task InsertOrSkipAsync<T>(string tableName, string[] onConflictColumns, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      return InsertOrSkipAsync(
         tableName,
         new UpsertConfiguration {
            OnConflictColumns = onConflictColumns
         },
         items,
         columnValueMapping,
         ct
      );
   }

   /// <summary>
   ///   Do an insert on a table. Creates a temp-table, copies the data to it and then does an insert on the original table. Rows that conflict with existing data are skipped.
   /// </summary>
   /// <param name="tableName">The table to do the insert on</param>
   /// <param name="upsertConfiguration">The insert configuration to use</param>
   /// <param name="items">The data to insert</param>
   /// <param name="columnValueMapping">The data-to-column mapping</param>
   /// <param name="ct">The cancellation token</param>
   /// <typeparam name="T">The Type of the data to insert</typeparam>
   /// <returns>A task for asynchronous awaiting.</returns>
   [SuppressMessage("ReSharper", "PossibleMultipleEnumeration", Justification = "Multiple enumerations are not a problem here.")]
   public async Task InsertOrSkipAsync<T>(string tableName, UpsertConfiguration upsertConfiguration, IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      if (!items.Any())
         return;

      var tempTableName = $"temp_{Guid.NewGuid():N}";
      var allColumns = columnValueMapping.Keys;

      await _db.InTransactionAsync(
         async () => {
            await _db.Dapper.ExecuteAsync($"CREATE TEMP TABLE {tempTableName} (LIKE {tableName} INCLUDING CONSTRAINTS INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY);");

            await CopyAsync(tempTableName, items, columnValueMapping, ct);

            var onConflictWhereClause = string.IsNullOrEmpty(upsertConfiguration.OnConflictWhereClause)
               ? string.Empty
               : $"WHERE {upsertConfiguration.OnConflictWhereClause}";

            await _db.Dapper.ExecuteAsync(
               $"""
                INSERT INTO {tableName} ({string.Join(", ", allColumns)})
                SELECT {string.Join(", ", allColumns)}
                FROM {tempTableName}
                ON CONFLICT ({string.Join(", ", upsertConfiguration.OnConflictColumns)}) {onConflictWhereClause } DO NOTHING
                """
            );
         }
      );
   }
}
