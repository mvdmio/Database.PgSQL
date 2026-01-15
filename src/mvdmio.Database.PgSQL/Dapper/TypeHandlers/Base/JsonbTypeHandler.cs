using Dapper;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.Text.Json;

namespace mvdmio.Database.PgSQL.Dapper.TypeHandlers.Base;

/// <summary>
///   Generic type handler for mapping JSONB columns to .NET objects.
/// </summary>
public class JsonbTypeHandler<T> : SqlMapper.TypeHandler<T>
{
   /// <inheritdoc />
   public override void SetValue(IDbDataParameter parameter, T? value)
   {
      if (value is null)
         parameter.Value = DBNull.Value;
      else
         parameter.Value = JsonSerializer.Serialize(value);

      if (parameter is NpgsqlParameter npgsqlParameter)
         npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
   }

   /// <inheritdoc />
   public override T? Parse(object value)
   {
      if (value is null or DBNull)
         return default;

      if (value is not string stringValue)
         return default;

      return JsonSerializer.Deserialize<T>(stringValue);
   }
}
