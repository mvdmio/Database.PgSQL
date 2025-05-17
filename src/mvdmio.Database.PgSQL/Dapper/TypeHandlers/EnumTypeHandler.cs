using System.Data;
using Dapper;

namespace mvdmio.Database.PgSQL.Dapper.TypeHandlers;

/// <summary>
///   Generic class for mapping enums to strings.
/// </summary>
public class EnumAsStringTypeHandler<T> : SqlMapper.TypeHandler<T>
   where T : struct, Enum
{
   /// <inheritdoc />
   public override void SetValue(IDbDataParameter parameter, T value)
   {
      parameter.Value = value.ToString();
   }

   /// <inheritdoc />
   public override T Parse(object value)
   {
      var stringValue = Convert.ToString(value);
      if (stringValue is null)
         return default;

      return (T)Enum.Parse(typeof(T), stringValue);
   }
}