using Dapper;
using JetBrains.Annotations;
using System.Data;
using System.Globalization;

namespace mvdmio.Database.PgSQL.Dapper.TypeHandlers.Base;

/// <summary>
///   Generic class for mapping enums to strings.
/// </summary>
/// <remarks>
///   Only needed for hand-written Dapper SQL. A generated repository states each enum column's storage claim at the
///   point it binds the value, so it never consults this and cannot be affected by whether it is registered.
/// </remarks>
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
   /// <exception cref="ArgumentException">The column held null, which no enum member represents.</exception>
   /// <remarks>
   ///   Parsed case-insensitively, matching what Dapper does for a text column with no handler registered and what the
   ///   query surface does for the same column, so a stored value differing in case from the member name reads the same
   ///   way whichever path it arrives through.
   ///   <para>
   ///     Null is refused rather than answered with the enum's default. Dapper skips the member assignment for a null
   ///     column instead of calling this, so a nullable enum column reads back as null and never reaches here; a
   ///     consumer calling the handler directly gets told what happened rather than handed the zero member.
   ///   </para>
   /// </remarks>
   public override T Parse(object value)
   {
      if (value is null or DBNull)
         throw new ArgumentException($"Cannot parse null into {typeof(T).Name}. A nullable enum column needs a nullable property.", nameof(value));

      return Enum.Parse<T>(Convert.ToString(value, CultureInfo.InvariantCulture)!, ignoreCase: true);
   }
}
