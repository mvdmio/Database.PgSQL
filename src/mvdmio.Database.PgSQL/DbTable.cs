﻿using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL;

/// <summary>
///    Abstract base class for all database tables.
/// </summary>
[PublicAPI]
public abstract class DbTable<TRecord>
   where TRecord : DbRecord
{
   private readonly DatabaseConnection _db;

   /// <summary>
   ///    The name of the table in the database without schema.
   /// </summary>
   protected internal abstract string TableName { get; }

   /// <summary>
   ///    The schema of the table in the database.
   /// </summary>
   protected internal abstract string Schema { get; }

   /// <summary>
   ///    The column names of all columns in the table.
   /// </summary>
   protected internal abstract string[] Columns { get; }

   /// <summary>
   ///    The column names of the primary key columns in the table.
   /// </summary>
   protected internal abstract string[] PrimaryKeyColumns { get; }

   /// <summary>
   ///    The column names of the generated columns in the table.
   /// </summary>
   protected internal abstract string[] GeneratedColumns { get; }

   /// <summary>
   ///    The full name of the table in the database, including schema.
   /// </summary>
   protected internal string FullTableName => $"{Schema}.{TableName}";

   /// <summary>
   ///    The column names of all columns in the table except the primary key columns.
   /// </summary>
   protected internal string[] ColumnsExceptPrimaryKeys => Columns.Except(PrimaryKeyColumns).ToArray();

   /// <summary>
   ///    The column names of all columns in the table except the primary key columns.
   /// </summary>
   protected internal string[] ColumnsExceptGenerated => Columns.Except(GeneratedColumns).ToArray();

   /// <summary>
   ///    Constructor.
   /// </summary>
   protected DbTable(DatabaseConnection db)
   {
      _db = db;
   }

   /// <summary>
   ///    Retrieves a single record from the database by its primary key.
   /// </summary>
   public TRecord? Find(long id)
   {
      return _db.Dapper.QuerySingleOrDefault<TRecord>(
         $"SELECT {string.Join(", ", Columns)} FROM {FullTableName} WHERE {PrimaryKeyColumns[0]} = :id",
         new Dictionary<string, object?> {
            { PrimaryKeyColumns[0], id }
         }
      );
   }

   /// <summary>
   ///    Retrieves a single record from the database by its primary key asynchronously.
   /// </summary>
   public Task<TRecord?> FindAsync(long id)
   {
      return _db.Dapper.QuerySingleOrDefaultAsync<TRecord>(
         $"SELECT {string.Join(", ", Columns)} FROM {FullTableName} WHERE {PrimaryKeyColumns[0]} = :id",
         new Dictionary<string, object?> {
            { PrimaryKeyColumns[0], id }
         }
      );
   }

   /// <summary>
   ///    Inserts a new record into the database.
   /// </summary>
   public TRecord Insert(TRecord record)
   {
      return _db.Dapper.QuerySingle<TRecord>(
         $"""
          INSERT INTO {FullTableName} ({string.Join(", ", ColumnsExceptGenerated)}) 
          VALUES ({string.Join(", ", ColumnsExceptGenerated.Select(c => $":{c}"))})
          RETURNING {string.Join(", ", Columns)}
          """,
         ColumnsExceptGenerated.Select(x => new KeyValuePair<string, object?>(x, record.GetValue(x))).ToDictionary(x => x.Key, x => x.Value)
      );
   }

   /// <summary>
   ///    Inserts a new record into the database asynchronously.
   /// </summary>
   public Task<TRecord> InsertAsync(TRecord record)
   {
      return _db.Dapper.QuerySingleAsync<TRecord>(
         $"""
          INSERT INTO {FullTableName} ({string.Join(", ", ColumnsExceptGenerated)}) 
          VALUES ({string.Join(", ", ColumnsExceptGenerated.Select(c => $":{c}"))})
          RETURNING {string.Join(", ", Columns)}
          """,
         ColumnsExceptGenerated.Select(x => new KeyValuePair<string, object?>(x, record.GetValue(x))).ToDictionary(x => x.Key, x => x.Value)
      );
   }
}