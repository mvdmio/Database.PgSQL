using JetBrains.Annotations;
using System.Reflection;

namespace mvdmio.Database.PgSQL.Migrations;

/// <summary>
///    Discovers and reads embedded schema resources from assemblies.
///    Schema files are automatically embedded when placed in a Schemas/ folder
///    in projects that reference the mvdmio.Database.PgSQL NuGet package.
/// </summary>
[PublicAPI]
public static class EmbeddedSchemaDiscovery
{
   private const string SCHEMA_PREFIX = "schema.";
   private const string SCHEMA_SUFFIX = ".sql";
   private const string DEFAULT_SCHEMA_NAME = "schema.sql";

   /// <summary>
   ///    Finds an embedded schema resource in the given assemblies.
   /// </summary>
   /// <param name="assemblies">The assemblies to search for embedded schema resources.</param>
   /// <param name="environment">
   ///    Optional environment name. If specified, looks for schema.{environment}.sql (case-insensitive).
   ///    Falls back to schema.sql if environment-specific file is not found.
   /// </param>
   /// <returns>
   ///    A tuple containing the resource stream and resource name if found, or null if no schema resource exists.
   ///    The caller is responsible for disposing the stream.
   /// </returns>
   public static (Stream Stream, string ResourceName)? FindSchemaResource(Assembly[] assemblies, string? environment)
   {
      if (assemblies.Length == 0)
         return null;

      if (!string.IsNullOrWhiteSpace(environment))
      {
         var envSchemaName = $"{SCHEMA_PREFIX}{environment.ToLowerInvariant()}{SCHEMA_SUFFIX}";
         var envResult = FindResourceByName(assemblies, envSchemaName);

         if (envResult is not null)
            return envResult;

         return FindResourceByName(assemblies, DEFAULT_SCHEMA_NAME);
      }

      var defaultResult = FindResourceByName(assemblies, DEFAULT_SCHEMA_NAME);

      if (defaultResult is not null)
         return defaultResult;

      return FindAnySchemaResource(assemblies);
   }

   /// <summary>
   ///    Finds all embedded schema resources across all assemblies, preserving assembly order.
   ///    Each assembly contributes at most one schema file. For a given assembly, the lookup uses
   ///    the same rules as <see cref="FindSchemaResource"/>: when an environment is specified, the
   ///    environment-specific schema is preferred with fallback to <c>schema.sql</c>; when no
   ///    environment is specified, <c>schema.sql</c> is preferred with fallback to any
   ///    <c>schema.*.sql</c>. Assemblies without a matching schema resource are silently skipped.
   /// </summary>
   /// <param name="assemblies">The assemblies to search for embedded schema resources.</param>
   /// <param name="environment">
   ///    Optional environment name. If specified, looks for schema.{environment}.sql (case-insensitive)
   ///    per assembly, falling back to schema.sql within that assembly.
   /// </param>
   /// <returns>
   ///    A list of (stream, resource name, source assembly) tuples in assembly order, one per assembly
   ///    that contains a matching schema resource. The caller is responsible for disposing all streams.
   /// </returns>
   public static IReadOnlyList<(Stream Stream, string ResourceName, Assembly Assembly)> FindAllSchemaResources(Assembly[] assemblies, string? environment)
   {
      var results = new List<(Stream Stream, string ResourceName, Assembly Assembly)>();

      if (assemblies.Length == 0)
         return results;

      foreach (var assembly in assemblies)
      {
         var resource = FindSchemaResourceInAssembly(assembly, environment);

         if (resource is not null)
            results.Add((resource.Value.Stream, resource.Value.ResourceName, assembly));
      }

      return results;
   }

   private static (Stream Stream, string ResourceName)? FindSchemaResourceInAssembly(Assembly assembly, string? environment)
   {
      var singleAssembly = new[] { assembly };

      if (!string.IsNullOrWhiteSpace(environment))
      {
         var envSchemaName = $"{SCHEMA_PREFIX}{environment.ToLowerInvariant()}{SCHEMA_SUFFIX}";
         var envResult = FindResourceByName(singleAssembly, envSchemaName);

         if (envResult is not null)
            return envResult;

         return FindResourceByName(singleAssembly, DEFAULT_SCHEMA_NAME);
      }

      var defaultResult = FindResourceByName(singleAssembly, DEFAULT_SCHEMA_NAME);

      if (defaultResult is not null)
         return defaultResult;

      return FindAnySchemaResource(singleAssembly);
   }

   /// <summary>
   ///    Reads the content of an embedded schema resource from the given assemblies.
   /// </summary>
   /// <param name="assemblies">The assemblies to search for embedded schema resources.</param>
   /// <param name="environment">
   ///    Optional environment name. If specified, looks for schema.{environment}.sql (case-insensitive).
   ///    Falls back to schema.sql if environment-specific file is not found.
   /// </param>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The content of the schema file, or null if no schema resource exists.</returns>
   public static async Task<string?> ReadSchemaContentAsync(
      Assembly[] assemblies,
      string? environment,
      CancellationToken cancellationToken = default)
   {
      var resource = FindSchemaResource(assemblies, environment);

      if (resource is null)
         return null;

      await using var stream = resource.Value.Stream;
      using var reader = new StreamReader(stream);
      return await reader.ReadToEndAsync(cancellationToken);
   }

   /// <summary>
   ///    Reads the content of all embedded schema resources across all assemblies in assembly order.
   /// </summary>
   /// <param name="assemblies">The assemblies to search for embedded schema resources.</param>
   /// <param name="environment">
   ///    Optional environment name. If specified, looks for schema.{environment}.sql (case-insensitive).
   ///    Falls back to schema.sql if environment-specific file is not found (per assembly).
   /// </param>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>
   ///    A list of (content, resource name, source assembly) tuples in assembly order, one per assembly
   ///    that contains a matching schema resource.
   /// </returns>
   public static async Task<IReadOnlyList<(string Content, string ResourceName, Assembly Assembly)>> ReadAllSchemaContentsAsync(
      Assembly[] assemblies,
      string? environment,
      CancellationToken cancellationToken = default)
   {
      var resources = FindAllSchemaResources(assemblies, environment);
      var results = new List<(string Content, string ResourceName, Assembly Assembly)>(resources.Count);

      foreach (var resource in resources)
      {
         await using var stream = resource.Stream;
         using var reader = new StreamReader(stream);
         var content = await reader.ReadToEndAsync(cancellationToken);
         results.Add((content, resource.ResourceName, resource.Assembly));
      }

      return results;
   }

   /// <summary>
   ///    Checks whether an embedded schema resource exists in the given assemblies.
   /// </summary>
   /// <param name="assemblies">The assemblies to search for embedded schema resources.</param>
   /// <param name="environment">
   ///    Optional environment name. If specified, looks for schema.{environment}.sql (case-insensitive).
   /// </param>
   /// <returns>True if a matching schema resource exists, false otherwise.</returns>
   public static bool SchemaResourceExists(Assembly[] assemblies, string? environment)
   {
      if (assemblies.Length == 0)
         return false;

      if (!string.IsNullOrWhiteSpace(environment))
      {
         var envSchemaName = $"{SCHEMA_PREFIX}{environment.ToLowerInvariant()}{SCHEMA_SUFFIX}";

         if (ResourceExists(assemblies, envSchemaName))
            return true;

         return ResourceExists(assemblies, DEFAULT_SCHEMA_NAME);
      }

      if (ResourceExists(assemblies, DEFAULT_SCHEMA_NAME))
         return true;

      return AnySchemaResourceExists(assemblies);
   }

   /// <summary>
   ///    Gets the name of the schema resource that would be used for the given environment.
   /// </summary>
   /// <param name="assemblies">The assemblies to search for embedded schema resources.</param>
   /// <param name="environment">
   ///    Optional environment name. If specified, looks for schema.{environment}.sql (case-insensitive).
   /// </param>
   /// <returns>The resource name if found, or null if no schema resource exists.</returns>
   public static string? GetSchemaResourceName(Assembly[] assemblies, string? environment)
   {
      var resource = FindSchemaResource(assemblies, environment);

      if (resource is null)
         return null;

      resource.Value.Stream.Dispose();
      return resource.Value.ResourceName;
   }

   private static (Stream Stream, string ResourceName)? FindResourceByName(Assembly[] assemblies, string resourceName)
   {
      foreach (var assembly in assemblies)
      {
         var manifestResourceNames = assembly.GetManifestResourceNames();

         var exactMatch = manifestResourceNames.FirstOrDefault(
            name => name.Equals(resourceName, StringComparison.OrdinalIgnoreCase));

         if (exactMatch is not null)
         {
            var stream = assembly.GetManifestResourceStream(exactMatch);

            if (stream is not null)
               return (stream, exactMatch);
         }

         var suffixMatch = manifestResourceNames.FirstOrDefault(
            name => name.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

         if (suffixMatch is not null)
         {
            var stream = assembly.GetManifestResourceStream(suffixMatch);

            if (stream is not null)
               return (stream, suffixMatch);
         }
      }

      return null;
   }

   private static (Stream Stream, string ResourceName)? FindAnySchemaResource(Assembly[] assemblies)
   {
      foreach (var assembly in assemblies)
      {
         var manifestResourceNames = assembly.GetManifestResourceNames();

         var schemaResource = manifestResourceNames.FirstOrDefault(name =>
         {
            var fileName = GetResourceFileName(name);
            return fileName.StartsWith(SCHEMA_PREFIX, StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(SCHEMA_SUFFIX, StringComparison.OrdinalIgnoreCase);
         });

         if (schemaResource is not null)
         {
            var stream = assembly.GetManifestResourceStream(schemaResource);

            if (stream is not null)
               return (stream, schemaResource);
         }
      }

      return null;
   }

   private static bool ResourceExists(Assembly[] assemblies, string resourceName)
   {
      foreach (var assembly in assemblies)
      {
         var manifestResourceNames = assembly.GetManifestResourceNames();

         if (manifestResourceNames.Any(name => name.Equals(resourceName, StringComparison.OrdinalIgnoreCase)))
            return true;

         if (manifestResourceNames.Any(name =>
                name.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase)))
         {
            return true;
         }
      }

      return false;
   }

   private static bool AnySchemaResourceExists(Assembly[] assemblies)
   {
      foreach (var assembly in assemblies)
      {
         var manifestResourceNames = assembly.GetManifestResourceNames();

         if (manifestResourceNames.Any(name =>
            {
               var fileName = GetResourceFileName(name);
               return fileName.StartsWith(SCHEMA_PREFIX, StringComparison.OrdinalIgnoreCase) &&
                      fileName.EndsWith(SCHEMA_SUFFIX, StringComparison.OrdinalIgnoreCase);
            }))
         {
            return true;
         }
      }

      return false;
   }

   private static string GetResourceFileName(string resourceName)
   {
      if (!resourceName.Contains('.'))
         return resourceName;

      var parts = resourceName.Split('.');

      if (parts.Length >= 2 && parts[^1].Equals("sql", StringComparison.OrdinalIgnoreCase))
      {
         for (var i = 0; i < parts.Length - 1; i++)
         {
            if (parts[i].Equals("schema", StringComparison.OrdinalIgnoreCase))
            {
               return string.Join(".", parts.Skip(i));
            }
         }
      }

      return resourceName;
   }
}
