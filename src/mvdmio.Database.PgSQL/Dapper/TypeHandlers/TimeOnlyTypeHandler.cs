﻿using System.Data;
using Dapper;

namespace mvdmio.Database.PgSQL.Dapper.TypeHandlers;

/// <summary>
///    Dapper handler for <see cref="TimeOnly" />
/// </summary>
/// <remarks>
///    Can be removed once Dapper decides to support TimeOnly out-of-the-box.
///    See: https://github.com/DapperLib/Dapper/issues/1715
/// </remarks>
public class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
   /// <inheritdoc />
   public override TimeOnly Parse(object value)
   {
      if (value is DateTime dateTime)
         return TimeOnly.FromDateTime(dateTime);

      if (value is TimeSpan timeSpan)
         return TimeOnly.FromTimeSpan(timeSpan);

      if (value is string str)
         return TimeOnly.Parse(str);

      throw new InvalidOperationException("Cannot parse value as TimeOnly");
   }

   /// <inheritdoc />
   public override void SetValue(IDbDataParameter parameter, TimeOnly value)
   {
      parameter.DbType = DbType.Time;
      parameter.Value = value;
   }
}