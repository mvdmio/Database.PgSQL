using LinqToDB;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

internal static class QueryStorageTypes
{
   private static readonly Dictionary<NpgsqlDbType, DataType> _dataTypes = new()
   {
      [NpgsqlDbType.Bigint] = DataType.Int64,
      [NpgsqlDbType.Bit] = DataType.BitArray,
      [NpgsqlDbType.Boolean] = DataType.Boolean,
      [NpgsqlDbType.Bytea] = DataType.Binary,
      [NpgsqlDbType.Char] = DataType.Char,
      [NpgsqlDbType.Date] = DataType.Date,
      [NpgsqlDbType.Double] = DataType.Double,
      [NpgsqlDbType.Integer] = DataType.Int32,
      [NpgsqlDbType.Interval] = DataType.Interval,
      [NpgsqlDbType.Json] = DataType.Json,
      [NpgsqlDbType.Jsonb] = DataType.BinaryJson,
      [NpgsqlDbType.Money] = DataType.Money,
      [NpgsqlDbType.Numeric] = DataType.Decimal,
      [NpgsqlDbType.Real] = DataType.Single,
      [NpgsqlDbType.Smallint] = DataType.Int16,
      [NpgsqlDbType.Text] = DataType.Text,
      [NpgsqlDbType.Time] = DataType.Time,
      [NpgsqlDbType.TimeTz] = DataType.TimeTZ,
      [NpgsqlDbType.Timestamp] = DataType.DateTime2,
      [NpgsqlDbType.TimestampTz] = DataType.DateTimeOffset,
      [NpgsqlDbType.Uuid] = DataType.Guid,
      [NpgsqlDbType.Varbit] = DataType.BitArray,
      [NpgsqlDbType.Varchar] = DataType.VarChar,
      [NpgsqlDbType.Xml] = DataType.Xml
   };

   public static DataType? DataTypeFor(NpgsqlDbType storedAs)
   {
      return _dataTypes.TryGetValue(storedAs, out var dataType) ? dataType : null;
   }

   public static IEnumerable<NpgsqlDbType> Representable => _dataTypes.Keys;
}
