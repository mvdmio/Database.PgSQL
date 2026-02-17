using Dapper;
using JetBrains.Annotations;
using System.Data;
using System.Globalization;

namespace mvdmio.Database.PgSQL.Dapper.TypeHandlers.Base;

/// <summary>
///   Generic class for mapping enums to strings.
/// </summary>
[PublicAPI]
public sealed class EnumAsStringTypeHandler<T> : SqlMapper.TypeHandler<T>
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
      var stringValue = Convert.ToString(value, CultureInfo.InvariantCulture);
      if (stringValue is null)
         return default;

      return Enum.Parse<T>(stringValue);
   }
}
