using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Models;

/// <summary>
/// Represents a value to be written to the database.
/// </summary>
public class DbValue
{
   /// <summary>
   /// The value to write.
   /// </summary>
   public object? Value { get; }

   /// <summary>
   /// The type of the value.
   /// </summary>
   public NpgsqlDbType Type { get; }

   /// <summary>
   ///   Constructor.
   /// </summary>
   public DbValue(object? value, NpgsqlDbType type)
   {
      Value = value;
      Type = type;
   }
}