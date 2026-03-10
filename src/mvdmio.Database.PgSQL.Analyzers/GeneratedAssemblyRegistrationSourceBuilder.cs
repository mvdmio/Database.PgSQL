using System.Collections.Immutable;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class GeneratedAssemblyRegistrationSourceBuilder
{
   public static string Build(string assemblyName, ImmutableArray<TableDefinitionModel> models)
   {
      var serviceName = ToPascalIdentifier(assemblyName);
      if (string.IsNullOrWhiteSpace(serviceName))
         serviceName = "GeneratedDatabase";

      var namespaceName = ToNamespaceName(assemblyName);
      if (string.IsNullOrWhiteSpace(namespaceName))
         namespaceName = "GeneratedDatabase";

      var className = $"MvdmioGenerated{serviceName}ServiceCollectionExtensions";
      var methodName = $"Add{serviceName}";

      var registrations = models
         .Select(x => new { x.NamespaceName, x.RepositoryInterfaceTypeName, x.RepositoryTypeName })
         .Distinct()
         .OrderBy(x => x.NamespaceName, StringComparer.Ordinal)
         .ThenBy(x => x.RepositoryInterfaceTypeName, StringComparer.Ordinal)
         .ToImmutableArray();

      var builder = new StringBuilder();
      builder.AppendLine("#nullable enable");
      builder.AppendLine("using global::Microsoft.Extensions.DependencyInjection;");
      builder.AppendLine("using global::Microsoft.Extensions.DependencyInjection.Extensions;");
      builder.AppendLine();
      builder.AppendLine($"namespace {namespaceName};");
      builder.AppendLine();
      builder.AppendLine($"public static class {className}");
      builder.AppendLine("{");
      builder.AppendLine($"   public static IServiceCollection {methodName}(this IServiceCollection services)");
      builder.AppendLine("   {");
      builder.AppendLine("      global::System.ArgumentNullException.ThrowIfNull(services);");
      builder.AppendLine("      global::mvdmio.Database.PgSQL.ServiceCollectionExtensions.AddDatabase(services);");
      builder.AppendLine();

      foreach (var registration in registrations)
      {
         var interfaceName = QualifyTypeName(registration.NamespaceName, registration.RepositoryInterfaceTypeName);
         var implementationName = QualifyTypeName(registration.NamespaceName, registration.RepositoryTypeName);
         builder.AppendLine($"      services.TryAddScoped<{interfaceName}, {implementationName}>();");
      }

      builder.AppendLine();
      builder.AppendLine("      return services;");
      builder.AppendLine("   }");
      builder.AppendLine("}");
      return builder.ToString();
   }

   private static string QualifyTypeName(string namespaceName, string typeName)
   {
      if (string.IsNullOrWhiteSpace(namespaceName))
         return $"global::{typeName}";

      return $"global::{namespaceName}.{typeName}";
   }

   private static string ToPascalIdentifier(string value)
   {
      if (string.IsNullOrWhiteSpace(value))
         return string.Empty;

      var builder = new StringBuilder(value.Length);
      var capitalizeNext = true;

      foreach (var character in value)
      {
         if (!char.IsLetterOrDigit(character))
         {
            capitalizeNext = true;
            continue;
         }

         if (builder.Length == 0 && char.IsDigit(character))
            builder.Append('_');

         builder.Append(capitalizeNext ? char.ToUpperInvariant(character) : character);
         capitalizeNext = false;
      }

      return builder.ToString();
   }

   private static string ToNamespaceName(string value)
   {
      if (string.IsNullOrWhiteSpace(value))
         return string.Empty;

      var segments = value.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
         .Select(ToPascalIdentifier)
         .Where(x => !string.IsNullOrWhiteSpace(x))
         .ToImmutableArray();

      return segments.Length == 0 ? string.Empty : string.Join(".", segments);
   }
}
