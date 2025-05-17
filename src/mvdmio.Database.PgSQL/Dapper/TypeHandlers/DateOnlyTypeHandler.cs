using System.Data;
using Dapper;

namespace mvdmio.Database.PgSQL.Dapper.TypeHandlers;

/// <summary>
///    Dapper handler for <see cref="DateOnly" />
/// </summary>
/// <remarks>
///    Can be removed once Dapper decides to support DateOnly out-of-the-box.
///    See: https://github.com/DapperLib/Dapper/issues/1715
/// </remarks>
public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
   /// <inheritdoc />
   public override DateOnly Parse(object value)
   {
      if (value is DateTime dateTime)
         return DateOnly.FromDateTime(dateTime);

      if (value is string str)
         return DateOnly.Parse(str);

      throw new InvalidOperationException("Cannot parse value as DateOnly");
   }

   /// <inheritdoc />
   public override void SetValue(IDbDataParameter parameter, DateOnly value)
   {
      parameter.DbType = DbType.Date;
      parameter.Value = value;
   }
}