using System.Runtime.CompilerServices;

namespace mvdmio.Database.PgSQL.Tests.Unit;

public static class VerifyInitializer
{
   [ModuleInitializer]
   public static void Initialize()
   {
      VerifySourceGenerators.Initialize();
   }
}