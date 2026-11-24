using Microsoft.CodeAnalysis;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class MigrationSymbols
{
   public static bool IsConcreteMigration(INamedTypeSymbol type)
   {
      return type is { TypeKind: TypeKind.Class, IsAbstract: false } && MigrationInterface.FindOn(type) is not null;
   }
}
