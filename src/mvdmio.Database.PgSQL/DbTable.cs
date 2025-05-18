namespace mvdmio.Database.PgSQL;

/// <summary>
///    Abstract base class for all database tables.
/// </summary>
public abstract class DbTable<TEntity>
{
   private readonly DatabaseConnection _db;

   /// <summary>
   ///    The name of the table in the database without schema.
   /// </summary>
   protected abstract string TableName { get; }

   /// <summary>
   ///    The schema of the table in the database.
   /// </summary>
   protected abstract string Schema { get; }

   /// <summary>
   ///    The column names of all columns in the table.
   /// </summary>
   protected abstract string[] Columns { get; }

   /// <summary>
   ///    The column names of the primary key columns in the table.
   /// </summary>
   protected abstract string[] PrimaryKeyColumns { get; }

   /// <summary>
   ///    The full name of the table in the database, including schema.
   /// </summary>
   protected string FullTableName => $"{Schema}.{TableName}";

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
   public TEntity? Find(long id)
   {
      return _db.Dapper.QuerySingleOrDefault<TEntity>(
         $"SELECT {string.Join(", ", Columns)} FROM {FullTableName} WHERE {PrimaryKeyColumns[0]} = :id",
         new Dictionary<string, object?> {
            { PrimaryKeyColumns[0], id }
         }
      );
   }
}