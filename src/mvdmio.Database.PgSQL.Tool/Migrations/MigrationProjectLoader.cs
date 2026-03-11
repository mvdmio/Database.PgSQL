using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Tool.Building;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Tool.Migrations;

/// <summary>
///    Builds the configured project and loads its migrations.
/// </summary>
internal class MigrationProjectLoader
{
   private readonly IProjectAssemblyBuilder _assemblyBuilder;

   public MigrationProjectLoader()
      : this(new ProjectAssemblyBuilder())
   {
   }

   internal MigrationProjectLoader(IProjectAssemblyBuilder assemblyBuilder)
   {
      _assemblyBuilder = assemblyBuilder;
   }

   public virtual MigrationProjectContext Load(string projectPath)
   {
      var assembly = _assemblyBuilder.BuildAndLoadAssembly(projectPath);
      var migrationRetriever = new ReflectionMigrationRetriever(assembly);
      var migrations = migrationRetriever.RetrieveMigrations().OrderBy(x => x.Identifier).ToArray();

      return new MigrationProjectContext(assembly, migrationRetriever, migrations);
   }
}

internal sealed record MigrationProjectContext(
   Assembly Assembly,
   IMigrationRetriever MigrationRetriever,
   IReadOnlyList<IDbMigration> Migrations
);

internal interface IProjectAssemblyBuilder
{
   Assembly BuildAndLoadAssembly(string projectPath);
}

internal sealed class ProjectAssemblyBuilder : IProjectAssemblyBuilder
{
   public Assembly BuildAndLoadAssembly(string projectPath)
   {
      return ProjectBuilder.BuildAndLoadAssembly(projectPath);
   }
}
