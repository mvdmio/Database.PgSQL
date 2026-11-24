using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Migrations;

internal static class PostDeployPart
{
   public static bool Exists(IDbMigration migration)
   {
      var interfaceMap = migration.GetType().GetInterfaceMap(typeof(IDbMigration));
      var index = Array.FindIndex(interfaceMap.InterfaceMethods, method => method.Name == nameof(IDbMigration.PostDeployAsync));

      return interfaceMap.TargetMethods[index]?.DeclaringType is { IsInterface: false };
   }
}
