namespace mvdmio.Database.PgSQL;

/// <summary>
///   Abstract base class for all database tables.
/// </summary>
public abstract class DbTable
{
   /// <summary>
   ///   The name of the table in the database without schema.
   /// </summary>
   public abstract string TableName { get; }
   
   /// <summary>
   ///   The schema of the table in the database.
   /// </summary>
   public abstract string Schema { get; }
   
   /// <summary>
   ///   The column names of all columns in the table.
   /// </summary>
   public abstract string[] Columns { get; }
   
   /// <summary>
   ///   The column names of the primary key columns in the table.
   /// </summary>
   public abstract string[] PrimaryKeyColumns { get; }
   
   /// <summary>
   ///   The full name of the table in the database, including schema.
   /// </summary>
   public string FullTableName => $"{Schema}.{TableName}";
}