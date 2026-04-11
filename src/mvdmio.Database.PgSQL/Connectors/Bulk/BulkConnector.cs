using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Models;
using Npgsql;
using NpgsqlTypes;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Connectors.Bulk;

/// <summary>
///    Connector for bulk-copying data to the database.
/// </summary>
[PublicAPI]
public sealed partial class BulkConnector
{
   private readonly DatabaseConnection _db;

   /// <summary>
   ///    Initializes a new instance of the <see cref="BulkConnector"/> class.
   /// </summary>
   /// <param name="db">The database connection to use for bulk operations.</param>
   public BulkConnector(DatabaseConnection db)
   {
      _db = db;
   }

   /// <summary>
   ///   Begin a binary copy session.
   /// </summary>
   /// <typeparam name="T">The type of items to be written to the database.</typeparam>
   /// <param name="tableName">The name of the target table for the copy operation.</param>
   /// <param name="columnValueMapping">A dictionary mapping column names to functions that extract values from items of type <typeparamref name="T"/>.</param>
   /// <param name="ct">A cancellation token to observe while waiting for the task to complete.</param>
   /// <returns>A <see cref="CopySession{T}"/> that can be used to write items to the database.</returns>
   public async Task<CopySession<T>> BeginCopyAsync<T>(string tableName, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      var copySession = new CopySession<T>(_db, tableName, columnValueMapping);
      await copySession.BeginAsync(ct);

      return copySession;
   }

   /// <summary>
   ///    Perform a binary copy to a given table.
   /// </summary>
   /// <typeparam name="T">The type of items to be written to the database.</typeparam>
   /// <param name="tableName">The name of the target table for the copy operation.</param>
   /// <param name="items">The items to copy to the database.</param>
   /// <param name="columnValueMapping">A dictionary mapping column names to functions that extract values from items of type <typeparamref name="T"/>.</param>
   /// <param name="ct">A cancellation token to observe while waiting for the task to complete.</param>
   /// <returns>A task representing the asynchronous copy operation.</returns>
   [SuppressMessage("ReSharper", "PossibleMultipleEnumeration", Justification = "Multiple enumerations are not a problem here.")]
   [SuppressMessage("Performance", "CA1851:Possible multiple enumerations of 'IEnumerable' collection", Justification = "Multiple enumerations are not a problem here.")]
   public async Task CopyAsync<T>(
      string tableName,
      IEnumerable<T> items,
      Dictionary<string, Func<T, DbValue>> columnValueMapping,
      CancellationToken ct = default
   )
   {
      if (!items.Any())
         return;

      var errors = new List<Exception>();

      var copySession = await BeginCopyAsync(tableName, columnValueMapping, ct);

      try
      {
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
      }
      finally
      {
         await copySession.DisposeAsync(ct);
      }

      if (errors.Count is 1)
         throw errors[0];

      if (errors.Count > 0)
         throw new AggregateException("Errors occurred during bulk copy.", errors);
   }

   /// <summary>
   ///    Creates a temporary table based on the column map, copies all items into it, and returns the created table name.
   /// </summary>
   /// <typeparam name="T">The type of items to be written to the database.</typeparam>
   /// <param name="items">The items to copy to the temporary table.</param>
   /// <param name="columnValueMapping">A dictionary mapping column names to functions that extract values from items of type <typeparamref name="T"/>.</param>
   /// <param name="ct">A cancellation token to observe while waiting for the task to complete.</param>
   /// <returns>The name of the created temporary table.</returns>
   public async Task<string> CopyToTempTableAsync<T>(IEnumerable<T> items, Dictionary<string, Func<T, DbValue>> columnValueMapping, CancellationToken ct = default)
   {
      var tableName = GenerateTempTableName();
      await CopyToTempTableAsync(
         tableName,
         items,
         columnValueMapping,
         ct
      );
      return tableName;
   }

   /// <summary>
   ///    Creates a temporary table based on the column map, copies all items into it, and returns the created table name.
   /// </summary>
   /// <typeparam name="T">The type of items to be written to the database.</typeparam>
   /// <param name="tableName">An optional temporary table name. When omitted, a unique name is generated.</param>
   /// <param name="items">The items to copy to the temporary table.</param>
   /// <param name="columnValueMapping">A dictionary mapping column names to functions that extract values from items of type <typeparamref name="T"/>.</param>
   /// <param name="ct">A cancellation token to observe while waiting for the task to complete.</param>
   /// <returns>The name of the created temporary table.</returns>
   [SuppressMessage("ReSharper", "PossibleMultipleEnumeration", Justification = "The items are materialized once for schema inference and copy execution.")]
   public async Task CopyToTempTableAsync<T>(
      string tableName,
      IEnumerable<T> items,
      Dictionary<string, Func<T, DbValue>> columnValueMapping,
      CancellationToken ct = default
   )
   {
      if (columnValueMapping.Count == 0)
         throw new ArgumentException("At least one column mapping is required.", nameof(columnValueMapping));

      var itemArray = items as T[] ?? items.ToArray();
      if (itemArray.Length == 0)
         throw new InvalidOperationException("CopyToTempTableAsync requires at least one item so column types can be inferred.");

      ValidateIdentifier(tableName, nameof(tableName));

      var createTableSql = BuildCreateTempTableSql(tableName, itemArray[0], columnValueMapping);

      await _db.Dapper.ExecuteAsync(createTableSql, ct: ct);
      await CopyAsync(
         tableName,
         itemArray,
         columnValueMapping,
         ct
      );
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
   /// <returns>The inserted and updated rows.</returns>
   public Task<IEnumerable<InsertOrUpdateResult<T>>> InsertOrUpdateAsync<T>(
      string tableName,
      string onConflictColumn,
      IEnumerable<T> items,
      Dictionary<string, Func<T, DbValue>> columnValueMapping,
      CancellationToken ct = default
   )
   {
      return InsertOrUpdateAsync(
         tableName,
         [onConflictColumn],
         items,
         columnValueMapping,
         ct
      );
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
   /// <returns>The inserted and updated rows.</returns>
   public Task<IEnumerable<InsertOrUpdateResult<T>>> InsertOrUpdateAsync<T>(
      string tableName,
      string[] onConflictColumns,
      IEnumerable<T> items,
      Dictionary<string, Func<T, DbValue>> columnValueMapping,
      CancellationToken ct = default
   )
   {
      return InsertOrUpdateAsync(
         tableName,
         new UpsertConfiguration { OnConflictColumns = onConflictColumns },
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
   /// <returns>The inserted and updated rows.</returns>
   [SuppressMessage("ReSharper", "PossibleMultipleEnumeration", Justification = "Multiple enumerations are not a problem here.")]
   public async Task<IEnumerable<InsertOrUpdateResult<T>>> InsertOrUpdateAsync<T>(
      string tableName,
      UpsertConfiguration upsertConfiguration,
      IEnumerable<T> items,
      Dictionary<string, Func<T, DbValue>> columnValueMapping,
      CancellationToken ct = default
   )
   {
      if (!items.Any())
         return [];

      var tempTableName = GenerateTempTableName();
      var allColumns = columnValueMapping.Keys;
      var updateColumns = allColumns.Where(x => !upsertConfiguration.OnConflictColumns.Contains(x)).ToArray();

      return await _db.InTransactionAsync(async () =>
      {
         await _db.Dapper.ExecuteAsync($"CREATE TEMP TABLE {tempTableName} (LIKE {tableName} INCLUDING CONSTRAINTS INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY);", ct: ct);

         await CopyAsync(
            tempTableName,
            items,
            columnValueMapping,
            ct
         );

         var onConflictWhereClause = string.IsNullOrEmpty(upsertConfiguration.OnConflictWhereClause) ? string.Empty : $"WHERE {upsertConfiguration.OnConflictWhereClause}";

         var query = $"""
            INSERT INTO {tableName} ({string.Join(", ", allColumns)})
            SELECT {string.Join(", ", allColumns)}
            FROM {tempTableName}
            ON CONFLICT ({string.Join(", ", upsertConfiguration.OnConflictColumns)}) {onConflictWhereClause} DO UPDATE
            SET {string.Join(", ", updateColumns.Select(x => $"{x} = EXCLUDED.{x}"))}
            WHERE ({string.Join(", ", updateColumns.Select(x => $"{tableName}.{x}"))}) IS DISTINCT FROM ({string.Join(", ", updateColumns.Select(x => $"EXCLUDED.{x}"))})
            RETURNING *, (xmax = 0) AS is_inserted, (xmax <> 0) AS is_updated
            """;

         return await _db.Dapper.QueryAsync<T, bool, bool, InsertOrUpdateResult<T>>(
            query,
            splitOn: "is_inserted, is_updated",
            (item, isInserted, isUpdated) => new InsertOrUpdateResult<T>
            {
               Item = item,
               IsInserted = isInserted,
               IsUpdated = isUpdated
            },
            ct: ct
         );
      });
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
   /// <returns>The inserted rows.</returns>
   public Task<IEnumerable<T>> InsertOrSkipAsync<T>(
      string tableName,
      string onConflictColumn,
      IEnumerable<T> items,
      Dictionary<string, Func<T, DbValue>> columnValueMapping,
      CancellationToken ct = default
   )
   {
      return InsertOrSkipAsync(
         tableName,
         [onConflictColumn],
         items,
         columnValueMapping,
         ct
      );
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
   /// <returns>The inserted rows.</returns>
   public Task<IEnumerable<T>> InsertOrSkipAsync<T>(
      string tableName,
      string[] onConflictColumns,
      IEnumerable<T> items,
      Dictionary<string, Func<T, DbValue>> columnValueMapping,
      CancellationToken ct = default
   )
   {
      return InsertOrSkipAsync(
         tableName,
         new UpsertConfiguration { OnConflictColumns = onConflictColumns },
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
   /// <returns>The inserted rows.</returns>
   [SuppressMessage("ReSharper", "PossibleMultipleEnumeration", Justification = "Multiple enumerations are not a problem here.")]
   public async Task<IEnumerable<T>> InsertOrSkipAsync<T>(
      string tableName,
      UpsertConfiguration upsertConfiguration,
      IEnumerable<T> items,
      Dictionary<string, Func<T, DbValue>> columnValueMapping,
      CancellationToken ct = default
   )
   {
      if (!items.Any())
         return [];

      var tempTableName = GenerateTempTableName();
      var allColumns = columnValueMapping.Keys;

      return await _db.InTransactionAsync(async () =>
      {
         await _db.Dapper.ExecuteAsync($"CREATE TEMP TABLE {tempTableName} (LIKE {tableName} INCLUDING CONSTRAINTS INCLUDING DEFAULTS INCLUDING GENERATED INCLUDING IDENTITY);", ct: ct);

         await CopyAsync(
            tempTableName,
            items,
            columnValueMapping,
            ct
         );

         var onConflictWhereClause = string.IsNullOrEmpty(upsertConfiguration.OnConflictWhereClause) ? string.Empty : $"WHERE {upsertConfiguration.OnConflictWhereClause}";

         return await _db.Dapper.QueryAsync<T>(
            $"""
            INSERT INTO {tableName} ({string.Join(", ", allColumns)})
            SELECT {string.Join(", ", allColumns)}
            FROM {tempTableName}
            ON CONFLICT ({string.Join(", ", upsertConfiguration.OnConflictColumns)}) {onConflictWhereClause} DO NOTHING
            RETURNING *
            """,
            ct: ct
         );
      });
   }

   private static string GenerateTempTableName()
   {
      return $"temp_{Guid.NewGuid():N}";
   }

   private static void ValidateIdentifier(string identifier, string parameterName)
   {
      if (ValidPgsqlIdentifierRegex().IsMatch(identifier))
         return;

      throw new ArgumentException("Only unquoted PostgreSQL identifiers containing letters, digits, and underscores are supported.", parameterName);
   }

   private static string BuildCreateTempTableSql<T>(string tableName, T sampleItem, Dictionary<string, Func<T, DbValue>> columnValueMapping)
   {
      var columnDefinitions = columnValueMapping.Select(column =>
      {
         var dbValue = column.Value.Invoke(sampleItem);
         var sqlType = MapToSqlType(dbValue.Type);

         return $"{column.Key} {sqlType}";
      });

      return $"CREATE TEMP TABLE {tableName} ({string.Join(", ", columnDefinitions)}) ON COMMIT DROP;";
   }

   private static string MapToSqlType(NpgsqlDbType type)
   {
      if (type is NpgsqlDbType.Unknown)
         throw new NotSupportedException("CopyToTempTableAsync requires explicit PostgreSQL types for temp table columns.");

      if ((type & NpgsqlDbType.Array) == NpgsqlDbType.Array)
         return $"{MapToSqlType(type & ~NpgsqlDbType.Array)}[]";

      return type switch
      {
         NpgsqlDbType.Smallint => "smallint",
         NpgsqlDbType.Integer => "integer",
         NpgsqlDbType.Bigint => "bigint",
         NpgsqlDbType.Real => "real",
         NpgsqlDbType.Double => "double precision",
         NpgsqlDbType.Numeric => "numeric",
         NpgsqlDbType.Text => "text",
         NpgsqlDbType.Varchar => "character varying",
         NpgsqlDbType.Char => "character",
         NpgsqlDbType.Boolean => "boolean",
         NpgsqlDbType.Timestamp => "timestamp without time zone",
         NpgsqlDbType.TimestampTz => "timestamp with time zone",
         NpgsqlDbType.Date => "date",
         NpgsqlDbType.Time => "time without time zone",
         NpgsqlDbType.TimeTz => "time with time zone",
         NpgsqlDbType.Uuid => "uuid",
         NpgsqlDbType.Json => "json",
         NpgsqlDbType.Jsonb => "jsonb",
         NpgsqlDbType.Bytea => "bytea",
         _ => throw new NotSupportedException($"CopyToTempTableAsync does not support PostgreSQL type '{type}'.")
      };
   }

   [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
   private static partial Regex ValidPgsqlIdentifierRegex();
}
