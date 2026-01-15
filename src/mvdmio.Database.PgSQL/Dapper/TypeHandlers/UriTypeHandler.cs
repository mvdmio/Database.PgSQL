using Dapper;
using System.Data;

namespace mvdmio.Database.PgSQL.Dapper.TypeHandlers;

public class UriTypeHandler : SqlMapper.TypeHandler<Uri>
{
   public override void SetValue(IDbDataParameter parameter, Uri? value)
   {
      if (value is null)
         parameter.Value = DBNull.Value;
      else
         parameter.Value = value.AbsoluteUri;
   }

   public override Uri? Parse(object value)
   {
      if (value is null or DBNull)
         return null;

      if (value is not string stringValue)
         return null;
      
      return new Uri(stringValue);
   }
}