using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tool.Building;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Tests.Unit.Building;

[Collection(nameof(ConsoleCollection))]
public sealed class ProjectBuilderTests : IDisposable
{
   private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"mvdmio-projectbuilder-tests-{Guid.NewGuid():N}");

   [Fact]
   public void ResolveCsprojPath_WithExplicitProjectFile_ReturnsThatPath()
   {
      var projectDirectory = CreateDirectory("ExplicitProject");
      var csprojPath = CreateProject(projectDirectory, "ExplicitProject");

      var result = ResolveCsprojPath(csprojPath);

      result.Should().Be(csprojPath);
   }

   [Fact]
   public void ResolveCsprojPath_WithDirectoryContainingSingleProject_ReturnsProjectPath()
   {
      var projectDirectory = CreateDirectory("SingleProject");
      var csprojPath = CreateProject(projectDirectory, "SingleProject");

      var result = ResolveCsprojPath(projectDirectory);

      result.Should().Be(csprojPath);
   }

   [Fact]
   public void ResolveCsprojPath_WithMultipleProjects_ThrowsHelpfulError()
   {
      var projectDirectory = CreateDirectory("MultipleProjects");
      CreateProject(projectDirectory, "One");
      CreateProject(projectDirectory, "Two");

      var action = () => ResolveCsprojPath(projectDirectory);

      action.Should().Throw<TargetInvocationException>()
         .WithInnerException<InvalidOperationException>()
         .WithMessage("Multiple .csproj files found in:*Specify the exact project path in .mvdmio-migrations.yml.");
   }

   [Fact]
   public void BuildAndLoadAssembly_WhenBuildFails_WritesBuildOutputAndThrows()
   {
      var projectDirectory = CreateDirectory("BrokenProject");
      var csprojPath = CreateProject(projectDirectory, "BrokenProject");
      File.WriteAllText(Path.Combine(projectDirectory, "BrokenClass.cs"), "public class BrokenClass { public void Run( };");

      using var errorWriter = new StringWriter();
      var originalError = Console.Error;
      Console.SetError(errorWriter);

      try
      {
         var action = () => ProjectBuilder.BuildAndLoadAssembly(csprojPath);

         action.Should().Throw<InvalidOperationException>()
            .WithMessage("Build failed. See output above for details.");

         errorWriter.ToString().Should().Contain("Build output:");
         errorWriter.ToString().Should().Contain("BrokenClass.cs");
      }
      finally
      {
         Console.SetError(originalError);
      }
   }

   public void Dispose()
   {
      if (Directory.Exists(_tempDirectory))
         Directory.Delete(_tempDirectory, true);
   }

   private static string ResolveCsprojPath(string projectPath)
   {
      var method = typeof(ProjectBuilder).GetMethod("ResolveCsprojPath", BindingFlags.NonPublic | BindingFlags.Static)!;
      return (string)method.Invoke(null, [projectPath])!;
   }

   private string CreateDirectory(string name)
   {
      var directory = Path.Combine(_tempDirectory, name);
      Directory.CreateDirectory(directory);
      return directory;
   }

   private static string CreateProject(string projectDirectory, string projectName)
   {
      var csprojPath = Path.Combine(projectDirectory, $"{projectName}.csproj");
      File.WriteAllText(
         csprojPath,
         "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>"
      );

      File.WriteAllText(Path.Combine(projectDirectory, "Class1.cs"), "public class Class1 { }");
      return csprojPath;
   }
}

[CollectionDefinition(nameof(ConsoleCollection), DisableParallelization = true)]
public sealed class ConsoleCollection : ICollectionFixture<ConsoleFixture>
{
}

public sealed class ConsoleFixture;
