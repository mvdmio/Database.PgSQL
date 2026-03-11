using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tool.Scaffolding;

namespace mvdmio.Database.PgSQL.Tests.Unit.Scaffolding;

public sealed class NamespaceResolverTests : IDisposable
{
   private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"mvdmio-namespace-tests-{Guid.NewGuid():N}");

   [Fact]
   public void Resolve_WithRootNamespaceAndNestedOutputDirectory_ReturnsCombinedNamespace()
   {
      var projectDirectory = CreateProject("MyApp", "Demo.App");
      var outputDirectory = Path.Combine(projectDirectory, "Generated", "Migrations");
      Directory.CreateDirectory(outputDirectory);

      var result = NamespaceResolver.Resolve(outputDirectory);

      result.Should().Be("Demo.App.Generated.Migrations");
   }

   [Fact]
   public void Resolve_WithoutRootNamespace_FallsBackToProjectName()
   {
      var projectDirectory = CreateProject("MyApp.Data");
      var outputDirectory = Path.Combine(projectDirectory, "Tables");
      Directory.CreateDirectory(outputDirectory);

      var result = NamespaceResolver.Resolve(outputDirectory);

      result.Should().Be("MyApp.Data.Tables");
   }

   [Fact]
   public void Resolve_FindsNearestProjectInParentDirectories()
   {
      var projectDirectory = CreateProject("Accounting", "Demo.Accounting");
      var outputDirectory = Path.Combine(projectDirectory, "Artifacts", "Generated", "Tables");
      Directory.CreateDirectory(outputDirectory);

      var result = NamespaceResolver.Resolve(outputDirectory);

      result.Should().Be("Demo.Accounting.Artifacts.Generated.Tables");
   }

   public void Dispose()
   {
      if (Directory.Exists(_tempDirectory))
         Directory.Delete(_tempDirectory, true);
   }

   private string CreateProject(string projectName, string? rootNamespace = null)
   {
      var projectDirectory = Path.Combine(_tempDirectory, projectName);
      Directory.CreateDirectory(projectDirectory);

      var rootNamespaceElement = rootNamespace is null
         ? string.Empty
         : $"<RootNamespace>{rootNamespace}</RootNamespace>";

      File.WriteAllText(
         Path.Combine(projectDirectory, $"{projectName}.csproj"),
         $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework>{rootNamespaceElement}</PropertyGroup></Project>"
      );

      return projectDirectory;
   }
}
