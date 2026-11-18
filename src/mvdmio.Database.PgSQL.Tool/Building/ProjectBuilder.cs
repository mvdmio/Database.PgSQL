using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

namespace mvdmio.Database.PgSQL.Tool.Building;

internal static class ProjectBuilder
{
   private const string DISABLE_BUILD_SERVERS_FLAG = "--disable-build-servers";

   public static async Task<Assembly> BuildAndLoadAssemblyAsync(string projectPath, CancellationToken cancellationToken, Action<int>? onProcessStarted = null)
   {
      cancellationToken.ThrowIfCancellationRequested();

      var csprojPath = ResolveCsprojPath(projectPath);
      var projectDir = Path.GetDirectoryName(csprojPath)!;

      Console.WriteLine($"Building project: {csprojPath}");

      using var process = new Process
      {
         StartInfo = new ProcessStartInfo
         {
            FileName = "dotnet",
            Arguments = $"build \"{csprojPath}\" {DISABLE_BUILD_SERVERS_FLAG} --nologo --verbosity quiet",
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
         }
      };

      process.Start();
      onProcessStarted?.Invoke(process.Id);

      var stdoutTask = process.StandardOutput.ReadToEndAsync();
      var stderrTask = process.StandardError.ReadToEndAsync();

      try
      {
         await process.WaitForExitAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
         if (!process.HasExited)
            process.Kill(entireProcessTree: true);

         await process.WaitForExitAsync(CancellationToken.None);
         throw;
      }

      var stdout = await stdoutTask;
      var stderr = await stderrTask;

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
      ProjectDependencyResolver.Register(assemblyPath, projectDir);
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
         return "net10.0";

      var version = framework.FrameworkName.Split("Version=v")[^1];
      return $"net{version}";
   }
}
