using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Models;

/// <summary>
/// Represents a value to be written to the database.
/// </summary>
public readonly struct DbValue : IEquatable<DbValue>
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
   public DbValue(object? value)
   {
      Value = value;
      Type = NpgsqlDbType.Unknown; // Default type if not specified
   }

   /// <summary>
   ///   Constructor.
   /// </summary>
   public DbValue(object? value, NpgsqlDbType type)
   {
      Value = value;
      Type = type;
   }

   /// <summary>
   ///   Implicit conversion for <see cref="string"/> values.
   /// </summary>
   public static implicit operator DbValue(string? value) => new(value, NpgsqlDbType.Text);

   /// <summary>
   ///   Implicit conversion for <see cref="bool"/> values.
   /// </summary>
   public static implicit operator DbValue(bool? value) => new(value, NpgsqlDbType.Boolean);

   /// <summary>
   ///   Implicit conversion for <see cref="short"/> values.
   /// </summary>
   public static implicit operator DbValue(short? value) => new(value, NpgsqlDbType.Smallint);

   /// <summary>
   ///   Implicit conversion for <see cref="int"/> values.
   /// </summary>
   public static implicit operator DbValue(int? value) => new(value, NpgsqlDbType.Integer);

   /// <summary>
   ///   Implicit conversion for <see cref="long"/> values.
   /// </summary>
   public static implicit operator DbValue(long? value) => new(value, NpgsqlDbType.Bigint);

   /// <summary>
   ///   Implicit conversion for <see cref="float"/> values.
   /// </summary>
   public static implicit operator DbValue(float? value) => new(value, NpgsqlDbType.Real);

   /// <summary>
   ///   Implicit conversion for <see cref="double"/> values.
   /// </summary>
   public static implicit operator DbValue(double? value) => new(value, NpgsqlDbType.Double);

   /// <summary>
   ///   Implicit conversion for <see cref="DateTime"/> values.
   /// </summary>
   public static implicit operator DbValue(DateTime? value) =>  new(value?.ToUniversalTime(), NpgsqlDbType.TimestampTz);

   /// <summary>
   ///   Implicit conversion for <see cref="DateTimeOffset"/> values.
   /// </summary>
   public static implicit operator DbValue(DateTimeOffset? value) =>  new(value?.ToUniversalTime(), NpgsqlDbType.TimestampTz);

   /// <summary>
   ///   Implicit conversion for <see cref="DateOnly"/> values.
   /// </summary>
   public static implicit operator DbValue(DateOnly? value) => new(value, NpgsqlDbType.Date);

   /// <summary>
   ///   Implicit conversion for <see cref="TimeOnly"/> values.
   /// </summary>
   public static implicit operator DbValue(TimeOnly? value) => new(value, NpgsqlDbType.Time);

   /// <inheritdoc />
   public bool Equals(DbValue other)
   {
      return Equals(Value, other.Value) && Type == other.Type;
   }

   /// <inheritdoc />
   public override bool Equals(object? obj)
   {
      if (obj is not DbValue other)
         return false;

      return Equals(Value, other.Value) && Type == other.Type;
   }

   /// <inheritdoc />
   public override int GetHashCode()
   {
      return HashCode.Combine(Value, Type);
   }

   /// <summary>
   ///   == operator.
   /// </summary>
   public static bool operator ==(DbValue left, DbValue right)
   {
      return left.Equals(right);
   }

   /// <summary>
   ///   != operator.
   /// </summary>
   public static bool operator !=(DbValue left, DbValue right)
   {
      return !left.Equals(right);
   }
}
