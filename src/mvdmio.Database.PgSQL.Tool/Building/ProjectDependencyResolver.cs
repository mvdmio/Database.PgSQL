using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace mvdmio.Database.PgSQL.Tool.Building;

/// <summary>
///    Resolves the NuGet package assemblies of a built project.
///    <para>
///       A class library's build output holds its own project references but not its packages, so a
///       migrations assembly loaded from <c>bin/</c> cannot load a type that names a package type. This reads
///       the project's <c>.deps.json</c> for the runtime assets of each package and finds them in the package
///       folders restore recorded in <c>obj/project.assets.json</c>.
///    </para>
///    <para>
///       An assembly the project does not list stays unresolved, so loading a type that needs it still fails.
///       The tool must never skip a type it cannot load: cleanup deletes the file of every migration it did not
///       discover.
///    </para>
/// </summary>
internal static class ProjectDependencyResolver
{
   private static readonly Dictionary<string, string> _assemblyPaths = new(StringComparer.OrdinalIgnoreCase);
   private static readonly object _lock = new();
   private static bool _registered;

   /// <summary>
   ///    Makes the package assemblies of the project that built <paramref name="assemblyPath" /> resolvable in the
   ///    default load context.
   /// </summary>
   /// <param name="assemblyPath">The built assembly, next to its <c>.deps.json</c>.</param>
   /// <param name="projectDir">The project directory, which holds <c>obj/project.assets.json</c>.</param>
   public static void Register(string assemblyPath, string projectDir)
   {
      var paths = ReadPackageAssemblyPaths(assemblyPath, projectDir);

      lock (_lock)
      {
         foreach (var (name, path) in paths)
            _assemblyPaths.TryAdd(name, path);

         if (_registered)
            return;

         AssemblyLoadContext.Default.Resolving += Resolve;
         _registered = true;
      }
   }

   internal static IReadOnlyDictionary<string, string> ReadPackageAssemblyPaths(string assemblyPath, string projectDir)
   {
      var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var depsPath = Path.ChangeExtension(assemblyPath, ".deps.json");

      if (!File.Exists(depsPath))
         return result;

      var packageFolders = ReadPackageFolders(projectDir);

      using var deps = JsonDocument.Parse(File.ReadAllText(depsPath));
      var root = deps.RootElement;

      if (!root.TryGetProperty("runtimeTarget", out var runtimeTarget) || !runtimeTarget.TryGetProperty("name", out var targetName))
         return result;

      if (!root.TryGetProperty("targets", out var targets) || !targets.TryGetProperty(targetName.GetString()!, out var target))
         return result;

      if (!root.TryGetProperty("libraries", out var libraries))
         return result;

      foreach (var entry in target.EnumerateObject())
      {
         if (!entry.Value.TryGetProperty("runtime", out var runtime))
            continue;

         if (!libraries.TryGetProperty(entry.Name, out var library))
            continue;

         if (library.GetProperty("type").GetString() != "package" || !library.TryGetProperty("path", out var libraryPath))
            continue;

         foreach (var asset in runtime.EnumerateObject())
         {
            var file = packageFolders
               .Select(folder => Path.Combine(folder, libraryPath.GetString()!, asset.Name))
               .FirstOrDefault(File.Exists);

            if (file is not null)
               result.TryAdd(Path.GetFileNameWithoutExtension(asset.Name), file);
         }
      }

      return result;
   }

   private static string[] ReadPackageFolders(string projectDir)
   {
      var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");

      if (File.Exists(assetsPath))
      {
         using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));

         if (assets.RootElement.TryGetProperty("packageFolders", out var folders))
            return folders.EnumerateObject().Select(x => x.Name).ToArray();
      }

      var nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");

      if (!string.IsNullOrWhiteSpace(nugetPackages))
         return [nugetPackages];

      return [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")];
   }

   private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
   {
      string? path;

      lock (_lock)
      {
         if (name.Name is null || !_assemblyPaths.TryGetValue(name.Name, out path))
            return null;
      }

      return context.LoadFromAssemblyPath(path);
   }
}
