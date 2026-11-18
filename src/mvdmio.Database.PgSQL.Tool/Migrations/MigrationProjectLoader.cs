using mvdmio.Database.PgSQL.Migrations.Interfaces;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers;
using mvdmio.Database.PgSQL.Migrations.MigrationRetrievers.Interfaces;
using mvdmio.Database.PgSQL.Tool.Building;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Tool.Migrations;

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

   public virtual async Task<MigrationProjectContext> LoadAsync(string projectPath, CancellationToken cancellationToken)
   {
      var assembly = await _assemblyBuilder.BuildAndLoadAssemblyAsync(projectPath, cancellationToken);
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
   Task<Assembly> BuildAndLoadAssemblyAsync(string projectPath, CancellationToken cancellationToken);
}

internal sealed class ProjectAssemblyBuilder : IProjectAssemblyBuilder
{
   public Task<Assembly> BuildAndLoadAssemblyAsync(string projectPath, CancellationToken cancellationToken)
   {
      return ProjectBuilder.BuildAndLoadAssemblyAsync(projectPath, cancellationToken);
   }
}
