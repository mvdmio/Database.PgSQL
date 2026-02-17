using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

namespace mvdmio.Database.PgSQL.Tool.Building;

/// <summary>
///    Builds a .NET project and loads the output assembly.
/// </summary>
internal static class ProjectBuilder
{
   /// <summary>
   ///    Builds the project at the given path and returns the loaded output assembly.
   /// </summary>
   /// <param name="projectPath">The absolute path to the project directory or .csproj file.</param>
   /// <returns>The loaded assembly containing migration classes.</returns>
   public static Assembly BuildAndLoadAssembly(string projectPath)
   {
      var csprojPath = ResolveCsprojPath(projectPath);
      var projectDir = Path.GetDirectoryName(csprojPath)!;

      Console.WriteLine($"Building project: {csprojPath}");

      var process = new Process
      {
         StartInfo = new ProcessStartInfo
         {
            FileName = "dotnet",
            Arguments = $"build \"{csprojPath}\" --nologo --verbosity quiet",
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
         }
      };

      process.Start();

      var stdout = process.StandardOutput.ReadToEnd();
      var stderr = process.StandardError.ReadToEnd();

      process.WaitForExit();

      if (process.ExitCode != 0)
      {
         Console.Error.WriteLine("Build output:");

         if (!string.IsNullOrWhiteSpace(stdout))
            Console.Error.WriteLine(stdout);

         if (!string.IsNullOrWhiteSpace(stderr))
            Console.Error.WriteLine(stderr);

         throw new InvalidOperationException("Build failed. See output above for details.");
      }

      Console.WriteLine("Build succeeded.");

      var assemblyPath = FindOutputAssembly(csprojPath);
      return Assembly.LoadFrom(assemblyPath);
   }

   private static string ResolveCsprojPath(string projectPath)
   {
      if (projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
      {
         if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project file not found: {projectPath}");

         return projectPath;
      }

      // projectPath is a directory - find the .csproj inside it
      if (!Directory.Exists(projectPath))
         throw new DirectoryNotFoundException($"Project directory not found: {projectPath}");

      var csprojFiles = Directory.GetFiles(projectPath, "*.csproj");

      return csprojFiles.Length switch
      {
         0 => throw new FileNotFoundException($"No .csproj file found in: {projectPath}"),
         1 => csprojFiles[0],
         _ => throw new InvalidOperationException(
            $"Multiple .csproj files found in: {projectPath}. " +
            "Specify the exact project path in .mvdmio-migrations.yml."
         )
      };
   }

   private static string FindOutputAssembly(string csprojPath)
   {
      var projectDir = Path.GetDirectoryName(csprojPath)!;
      var assemblyName = Path.GetFileNameWithoutExtension(csprojPath);
      var tfm = GetCurrentTargetFrameworkMoniker();

      // Look in bin/Debug/<tfm>/ first, then bin/Release/<tfm>/
      string[] configurations = ["Debug", "Release"];

      foreach (var config in configurations)
      {
         var dllPath = Path.Combine(projectDir, "bin", config, tfm, $"{assemblyName}.dll");

         if (File.Exists(dllPath))
            return dllPath;
      }

      throw new FileNotFoundException(
         $"Could not find built assembly '{assemblyName}.dll' in bin/Debug/{tfm}/ or bin/Release/{tfm}/. " +
         "Make sure the project targets a compatible framework."
      );
   }

   private static string GetCurrentTargetFrameworkMoniker()
   {
      var framework = Assembly.GetExecutingAssembly()
         .GetCustomAttribute<TargetFrameworkAttribute>();

      if (framework?.FrameworkName is null)
         return "net8.0";

      // FrameworkName is like ".NETCoreApp,Version=v8.0" -> convert to "net8.0"
      var version = framework.FrameworkName.Split("Version=v")[^1];
      return $"net{version}";
   }
}
