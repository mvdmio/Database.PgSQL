using System.Reflection;
using JetBrains.Annotations;

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

      // If environment is specified, try environment-specific schema first
      if (!string.IsNullOrWhiteSpace(environment))
      {
         var envSchemaName = $"{SCHEMA_PREFIX}{environment.ToLowerInvariant()}{SCHEMA_SUFFIX}";
         var envResult = FindResourceByName(assemblies, envSchemaName);

         if (envResult is not null)
            return envResult;

         // Fall back to default schema.sql
         return FindResourceByName(assemblies, DEFAULT_SCHEMA_NAME);
      }

      // No environment specified: prefer schema.sql, then any schema.*.sql
      var defaultResult = FindResourceByName(assemblies, DEFAULT_SCHEMA_NAME);

      if (defaultResult is not null)
         return defaultResult;

      // Try to find any schema file
      return FindAnySchemaResource(assemblies);
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

         // Fall back to default schema.sql
         return ResourceExists(assemblies, DEFAULT_SCHEMA_NAME);
      }

      // No environment specified: check for schema.sql or any schema.*.sql
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

      // Dispose the stream since we only need the name
      resource.Value.Stream.Dispose();
      return resource.Value.ResourceName;
   }

   private static (Stream Stream, string ResourceName)? FindResourceByName(Assembly[] assemblies, string resourceName)
   {
      foreach (var assembly in assemblies)
      {
         var manifestResourceNames = assembly.GetManifestResourceNames();

         // Look for exact match first (LogicalName was set to just the filename)
         var exactMatch = manifestResourceNames.FirstOrDefault(
            name => name.Equals(resourceName, StringComparison.OrdinalIgnoreCase));

         if (exactMatch is not null)
         {
            var stream = assembly.GetManifestResourceStream(exactMatch);

            if (stream is not null)
               return (stream, exactMatch);
         }

         // Look for match at end of resource name (for resources without LogicalName)
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

         // Find any resource that looks like a schema file
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

         // Check for exact match
         if (manifestResourceNames.Any(name => name.Equals(resourceName, StringComparison.OrdinalIgnoreCase)))
            return true;

         // Check for suffix match
         if (manifestResourceNames.Any(name =>
                name.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase)))
            return true;
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
            return true;
      }

      return false;
   }

   private static string GetResourceFileName(string resourceName)
   {
      // Resource names can be either:
      // 1. Just the filename (when LogicalName is set): "schema.local.sql"
      // 2. Namespace-qualified: "MyApp.Data.Schemas.schema.local.sql"

      // If it contains no dots or only the expected dots, it's already a filename
      if (!resourceName.Contains('.'))
         return resourceName;

      // Try to extract just the schema.*.sql part
      var parts = resourceName.Split('.');

      // If the last part is "sql" and second-to-last starts with "schema", reconstruct
      if (parts.Length >= 2 && parts[^1].Equals("sql", StringComparison.OrdinalIgnoreCase))
      {
         // Find where "schema" starts in the parts
         for (var i = 0; i < parts.Length - 1; i++)
         {
            if (parts[i].Equals("schema", StringComparison.OrdinalIgnoreCase))
            {
               // Reconstruct from "schema" to end
               return string.Join(".", parts.Skip(i));
            }
         }
      }

      // Return the original name if we can't parse it
      return resourceName;
   }
}
