using Dapper;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.Text.Json;

namespace mvdmio.Database.PgSQL.Dapper.TypeHandlers;

/// <summary>
/// Dapper type handler for mapping JSON columns to <see cref="Dictionary{TKey, TValue}"/> with string keys and values.
/// </summary>
public sealed class JsonDictionaryTypeHandler : SqlMapper.TypeHandler<Dictionary<string, string>>
{
   /// <inheritdoc />
   public override void SetValue(IDbDataParameter parameter, Dictionary<string, string>? value)
   {
      if (value is null)
         parameter.Value = DBNull.Value;
      else
         parameter.Value = JsonSerializer.Serialize(value);

      if (parameter is NpgsqlParameter npgsqlParameter)
         npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
   }

   /// <inheritdoc />
   public override Dictionary<string, string>? Parse(object value)
   {
      if (value is null or DBNull)
         return null;

      if (value is not string stringValue)
         return null;

      return JsonSerializer.Deserialize<Dictionary<string, string>>(stringValue);
   }
}
