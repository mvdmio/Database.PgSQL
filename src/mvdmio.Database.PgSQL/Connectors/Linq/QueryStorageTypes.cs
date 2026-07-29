using LinqToDB;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    Translates a column's storage claim into the data type the query surface's provider understands.
/// </summary>
/// <remarks>
///    A claim is spelled with Npgsql's own <see cref="NpgsqlDbType" />, because on the Dapper surface the claim and the
///    wire representation are the same value and there is nothing to translate. Only the provider needs this table, and
///    only for the members it can represent — a claim absent from it is honoured on the Dapper surface and left unstated
///    here, which is what <c>PGSQL0024</c> warns about at build time.
///    <para>
///       The analyzer decides whether to warn from its own copy of which members are listed here. It cannot reference
///       this assembly, so the two are kept in step by hand: adding an entry here means adding the member there.
///    </para>
/// </remarks>
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

   /// <summary>
   ///    The provider data type a claim corresponds to, or <see langword="null" /> when the provider cannot represent it.
   /// </summary>
   public static DataType? DataTypeFor(NpgsqlDbType storedAs)
   {
      return _dataTypes.TryGetValue(storedAs, out var dataType) ? dataType : null;
   }

   /// <summary>Every claim this table can represent, which is what the analyzer's copy has to match.</summary>
   public static IEnumerable<NpgsqlDbType> Representable => _dataTypes.Keys;
}
