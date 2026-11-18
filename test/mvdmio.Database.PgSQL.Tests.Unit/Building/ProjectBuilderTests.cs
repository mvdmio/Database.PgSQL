using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tool.Building;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Tests.Unit.Building;

[Collection(nameof(ConsoleCollection))]
public sealed class ProjectBuilderTests : IDisposable
{
   private const int HOLD_SECONDS = 117;

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
   public async Task BuildAndLoadAssemblyAsync_WhenBuildFails_WritesBuildOutputAndThrows()
   {
      var projectDirectory = CreateDirectory("BrokenProject");
      var csprojPath = CreateProject(projectDirectory, "BrokenProject");
      File.WriteAllText(Path.Combine(projectDirectory, "BrokenClass.cs"), "public class BrokenClass { public void Run( };");

      using var errorWriter = new StringWriter();
      var originalError = Console.Error;
      Console.SetError(errorWriter);

      try
      {
         var action = () => ProjectBuilder.BuildAndLoadAssemblyAsync(csprojPath, TestContext.Current.CancellationToken);

         await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Build failed. See output above for details.");

         errorWriter.ToString().Should().Contain("Build output:");
         errorWriter.ToString().Should().Contain("BrokenClass.cs");
      }
      finally
      {
         Console.SetError(originalError);
      }
   }

   [Fact]
   public async Task BuildAndLoadAssemblyAsync_WhenTheBuildStartsFreshWorkers_Returns()
   {
      var projectDirectory = CreateDirectory("FreshWorkersProject");
      var csprojPath = CreateProject(projectDirectory, "FreshWorkersProject");
      var originalSalt = Environment.GetEnvironmentVariable("MSBUILDNODEHANDSHAKESALT");
      Environment.SetEnvironmentVariable("MSBUILDNODEHANDSHAKESALT", Guid.NewGuid().ToString("N"));

      try
      {
         var assembly = await ProjectBuilder.BuildAndLoadAssemblyAsync(csprojPath, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);

         assembly.GetTypes().Should().ContainSingle(x => x.Name == "Class1");
      }
      finally
      {
         Environment.SetEnvironmentVariable("MSBUILDNODEHANDSHAKESALT", originalSalt);
      }
   }

   [Fact]
   public async Task BuildAndLoadAssemblyAsync_WhenATypeDerivesFromAPackageType_LoadsEveryType()
   {
      var projectDirectory = CreateDirectory("PackageProject");
      var csprojPath = Path.Combine(projectDirectory, "PackageProject.csproj");
      File.WriteAllText(
         csprojPath,
         $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{CurrentTargetFramework()}</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><PackageReference Include=\"Humanizer.Core\" Version=\"2.14.1\" /></ItemGroup></Project>"
      );
      File.WriteAllText(Path.Combine(projectDirectory, "PackageType.cs"), "public class PackageType : Humanizer.Localisation.Formatters.DefaultFormatter { public PackageType() : base(\"en\") { } }");

      var assembly = await ProjectBuilder.BuildAndLoadAssemblyAsync(csprojPath, TestContext.Current.CancellationToken);

      assembly.GetTypes().Should().ContainSingle(x => x.Name == "PackageType")
         .Which.BaseType!.FullName.Should().Be("Humanizer.Localisation.Formatters.DefaultFormatter");
   }

   [Fact]
   public async Task BuildAndLoadAssemblyAsync_WhenTheProjectReferencesANewerVersionOfAToolPackage_LoadsEveryType()
   {
      var projectDirectory = CreateDirectory("NewerPackageProject");
      var csprojPath = Path.Combine(projectDirectory, "NewerPackageProject.csproj");
      File.WriteAllText(
         csprojPath,
         $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{CurrentTargetFramework()}</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><PackageReference Include=\"Npgsql\" Version=\"10.0.3\" /></ItemGroup></Project>"
      );
      File.WriteAllText(Path.Combine(projectDirectory, "NewerPackageType.cs"), "public class NewerPackageType : Npgsql.NpgsqlException { }");

      var assembly = await ProjectBuilder.BuildAndLoadAssemblyAsync(csprojPath, TestContext.Current.CancellationToken);

      assembly.GetTypes().Should().ContainSingle(x => x.Name == "NewerPackageType")
         .Which.BaseType!.FullName.Should().Be("Npgsql.NpgsqlException");
   }

   [Fact]
   public async Task BuildAndLoadAssemblyAsync_WhenCancelledAfterTheBuildStarted_KillsTheWholeProcessTreeAndThrowsOperationCanceledException()
   {
      var projectDirectory = CreateDirectory("HeldProject");
      var csprojPath = CreateHeldProject(projectDirectory, "HeldProject");
      var buildProcessId = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
      using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

      var build = ProjectBuilder.BuildAndLoadAssemblyAsync(csprojPath, cancellation.Token, buildProcessId.SetResult);
      var rootId = await buildProcessId.Task.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
      var tree = await WaitForHeldCommandAsync(rootId);
      await cancellation.CancelAsync();

      var action = () => build;
      await action.Should().ThrowAsync<OperationCanceledException>();

      ProcessTree.IsRunning(rootId).Should().BeFalse();
      var survivors = await WaitForTreeToExitAsync(tree);
      survivors.Should().BeEmpty();
   }

   [Fact]
   public async Task BuildAndLoadAssemblyAsync_WhenCancelledBeforeTheBuildStarts_ThrowsWithoutStartingAProcess()
   {
      var projectDirectory = CreateDirectory("CancelledProject");
      var csprojPath = CreateProject(projectDirectory, "CancelledProject");
      var processStarted = false;
      using var cancellation = new CancellationTokenSource();
      await cancellation.CancelAsync();

      var action = () => ProjectBuilder.BuildAndLoadAssemblyAsync(csprojPath, cancellation.Token, _ => processStarted = true);

      await action.Should().ThrowAsync<OperationCanceledException>();
      processStarted.Should().BeFalse();
   }

   public void Dispose()
   {
      if (!Directory.Exists(_tempDirectory))
         return;

      try
      {
         Directory.Delete(_tempDirectory, true);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
   }

   private static async Task<IReadOnlyCollection<int>> WaitForHeldCommandAsync(int rootId)
   {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      timeout.CancelAfter(TimeSpan.FromMinutes(2));

      while (true)
      {
         var tree = await ProcessTree.DescendantsAsync(rootId, timeout.Token);

         if (tree.Any(x => x.CommandLine.Contains(HOLD_SECONDS.ToString(), StringComparison.Ordinal)))
            return tree.Select(x => x.ProcessId).ToArray();
      }
   }

   private static async Task<IReadOnlyCollection<int>> WaitForTreeToExitAsync(IReadOnlyCollection<int> processIds)
   {
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

      while (true)
      {
         var survivors = processIds.Where(ProcessTree.IsRunning).ToArray();

         if (survivors.Length == 0 || timeout.IsCancellationRequested)
            return survivors;

         await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
      }
   }

   private static string CreateHeldProject(string projectDirectory, string projectName)
   {
      var csprojPath = Path.Combine(projectDirectory, $"{projectName}.csproj");
      File.WriteAllText(
         csprojPath,
         "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>" +
         "<Target Name=\"HoldTheBuild\" BeforeTargets=\"CoreCompile\">" +
         $"<Exec Condition=\"$([MSBuild]::IsOSPlatform('Windows'))\" Command=\"powershell -NoProfile -Command Start-Sleep -Seconds {HOLD_SECONDS}\" />" +
         $"<Exec Condition=\"!$([MSBuild]::IsOSPlatform('Windows'))\" Command=\"sleep {HOLD_SECONDS}\" />" +
         "</Target></Project>"
      );

      File.WriteAllText(Path.Combine(projectDirectory, "Class1.cs"), "public class Class1 { }");
      return csprojPath;
   }

   private static string CurrentTargetFramework()
   {
      var frameworkName = typeof(ProjectBuilder).Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()!.FrameworkName;
      return $"net{frameworkName.Split("Version=v")[^1]}";
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
         "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>"
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
