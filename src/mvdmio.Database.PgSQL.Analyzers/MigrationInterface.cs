using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class MigrationInterface
{
   private const string IDBMIGRATION_INTERFACE_NAME = "IDbMigration";
   private const string IDBMIGRATION_FULL_NAMESPACE = "mvdmio.Database.PgSQL.Migrations.Interfaces";

   public static INamedTypeSymbol? FindOn(INamedTypeSymbol type)
   {
      return type.AllInterfaces.FirstOrDefault(x => x.Name == IDBMIGRATION_INTERFACE_NAME && x.ContainingNamespace.ToDisplayString() == IDBMIGRATION_FULL_NAMESPACE);
   }
}
