using Dapper;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.Text.Json;

namespace mvdmio.Database.PgSQL.Dapper.TypeHandlers;

public class JsonDictionaryTypeHandler : SqlMapper.TypeHandler<Dictionary<string, string>>
{
   public override void SetValue(IDbDataParameter parameter, Dictionary<string, string>? value)
   {
      if (value is null)
         parameter.Value = DBNull.Value;
      else
         parameter.Value = JsonSerializer.Serialize(value);

      if (parameter is NpgsqlParameter npgsqlParameter)
         npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
   }

   public override Dictionary<string, string>? Parse(object value)
   {
      if (value is null or DBNull)
         return null;

      if (value is not string stringValue)
         return null;

      return JsonSerializer.Deserialize<Dictionary<string, string>>(stringValue);
   }
}
