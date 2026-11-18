using System.Xml.Linq;

namespace mvdmio.Database.PgSQL.Tool.Scaffolding;

internal static class NamespaceResolver
{
   public static string Resolve(string migrationsDirectory)
   {
      var csprojPath = FindNearestCsproj(migrationsDirectory) ?? throw new InvalidOperationException(
            $"Could not find a .csproj file in or above '{migrationsDirectory}'. " +
            "Make sure you are running the tool from within a .NET project."
         );

      var projectDir = Path.GetDirectoryName(csprojPath)!;
      var rootNamespace = ReadRootNamespace(csprojPath);

      var relativePath = Path.GetRelativePath(projectDir, migrationsDirectory);

      if (relativePath == ".")
         return rootNamespace;

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

      return Path.GetFileNameWithoutExtension(csprojPath);
   }
}
