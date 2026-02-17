using Dapper;
using mvdmio.Database.PgSQL.Dapper.TypeHandlers;

namespace mvdmio.Database.PgSQL.Dapper;

internal static class DefaultConfig
{
   static DefaultConfig()
   {
      DefaultTypeMap.MatchNamesWithUnderscores = true;
      SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
      SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
      SqlMapper.AddTypeHandler(new JsonDictionaryTypeHandler());
      SqlMapper.AddTypeHandler(new UriTypeHandler());
   }

   public static void EnsureInitialized()
   {
      // Don't have to do anything here.
   }
}
