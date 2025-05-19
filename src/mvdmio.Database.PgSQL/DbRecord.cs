namespace mvdmio.Database.PgSQL;

/// <summary>
///    Abstract base class for all database records.
/// </summary>
public abstract class DbRecord
{
   /// <summary>
   ///    Retrieves the value of a column by its name.
   /// </summary>
   public abstract object? GetValue(string column);
}