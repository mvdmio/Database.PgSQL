using System.Xml.Linq;

namespace mvdmio.Database.PgSQL.Tool.Scaffolding;

/// <summary>
///    Resolves the namespace for a generated migration file by inspecting the nearest .csproj.
/// </summary>
internal static class NamespaceResolver
{
   /// <summary>
   ///    Resolves the namespace for a migration file in the given output directory.
   /// </summary>
   /// <param name="migrationsDirectory">The absolute path to the migrations output directory.</param>
   /// <returns>The resolved namespace string.</returns>
   public static string Resolve(string migrationsDirectory)
   {

<<<<<<< TODO: Unmerged change from project 'mvdmio.Database.PgSQL.Tool(net9.0)', Before:
      var csprojPath = FindNearestCsproj(migrationsDirectory);

      if (csprojPath is null)
         throw new InvalidOperationException(
            $"Could not find a .csproj file in or above '{migrationsDirectory}'. " +
            "Make sure you are running the tool from within a .NET project."
         );
=======
      var csprojPath = FindNearestCsproj(migrationsDirectory) ?? throw new InvalidOperationException(
            $"Could not find a .csproj file in or above '{migrationsDirectory}'. " +
            "Make sure you are running the tool from within a .NET project."
         );
>>>>>>> After
      var csprojPath = FindNearestCsproj(migrationsDirectory) ?? throw new InvalidOperationException(
            $"Could not find a .csproj file in or above '{migrationsDirectory}'. " +
            "Make sure you are running the tool from within a .NET project."
         );

      var projectDir = Path.GetDirectoryName(csprojPath)!;
      var rootNamespace = ReadRootNamespace(csprojPath);

      // Compute the relative path from the project directory to the migrations directory
      var relativePath = Path.GetRelativePath(projectDir, migrationsDirectory);

      if (relativePath == ".")
         return rootNamespace;

      // Convert path separators to namespace separators
      var namespaceSuffix = relativePath
         .Replace(Path.DirectorySeparatorChar, '.')
         .Replace(Path.AltDirectorySeparatorChar, '.');

      return $"{rootNamespace}.{namespaceSuffix}";
   }

   private static string? FindNearestCsproj(string startDirectory)
   {
      var directory = new DirectoryInfo(startDirectory);

      while (directory is not null)
      {
         var csprojFiles = directory.GetFiles("*.csproj");

         if (csprojFiles.Length > 0)
            return csprojFiles[0].FullName;

         directory = directory.Parent;
      }

      return null;
   }

   private static string ReadRootNamespace(string csprojPath)
   {
      var doc = XDocument.Load(csprojPath);
      var rootNamespaceElement = doc.Descendants("RootNamespace").FirstOrDefault();

      if (rootNamespaceElement is not null)
         return rootNamespaceElement.Value;

      // Derive from the project file name (e.g. MyApp.csproj -> MyApp)
      return Path.GetFileNameWithoutExtension(csprojPath);
   }
}
