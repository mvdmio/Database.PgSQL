using System.Collections.Immutable;
using System.Text;

namespace mvdmio.Database.PgSQL.Analyzers;

internal static class GeneratedAssemblyRegistrationSourceBuilder
{
   public static string Build(string assemblyName, ImmutableArray<ResolvedTable> tables)
   {
      var serviceName = ToPascalIdentifier(assemblyName);
      if (string.IsNullOrWhiteSpace(serviceName))
         serviceName = "GeneratedDatabase";

      var namespaceName = ToNamespaceName(assemblyName);
      if (string.IsNullOrWhiteSpace(namespaceName))
         namespaceName = "GeneratedDatabase";

      var className = $"MvdmioGenerated{serviceName}ServiceCollectionExtensions";
      var methodName = $"Add{serviceName}";

      var registrations = tables
         .Select(x => new { x.Model.NamespaceName, x.Model.RepositoryInterfaceTypeName, x.Model.RepositoryTypeName })
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
      builder.AppendLine();
      AppendQueryMappingRegistration(builder, tables);
      builder.AppendLine("}");
      return builder.ToString();
   }

   private static void AppendQueryMappingRegistration(StringBuilder builder, ImmutableArray<ResolvedTable> tables)
   {
      var ordered = tables
         .OrderBy(x => x.Model.NamespaceName, StringComparer.Ordinal)
         .ThenBy(x => x.Model.DataTypeName, StringComparer.Ordinal)
         .ToImmutableArray();

      builder.AppendLine("   [global::System.Runtime.CompilerServices.ModuleInitializer]");
      builder.AppendLine("   internal static void RegisterQueryMappings()");
      builder.AppendLine("   {");

      foreach (var table in ordered)
      {
         var model = table.Model;

         builder.AppendLine($"      global::mvdmio.Database.PgSQL.Connectors.Linq.QueryMappings.Register<{QualifyTypeName(model.NamespaceName, model.DataTypeName)}>(");
         builder.AppendLine($"         {ToLiteral(model.SchemaName)},");
         builder.AppendLine($"         {ToLiteral(model.TableName)},");
         builder.AppendLine("         static entity => entity");

         foreach (var property in model.DataProperties)
         {
            builder.AppendLine($"            {ColumnCall(property)}");
         }

         foreach (var relation in table.Relations)
         {
            builder.AppendLine($"            {RelationCall(relation)}");
         }

         builder.AppendLine("      );");
      }

      builder.AppendLine("   }");
   }

   private static string ColumnCall(PropertyDefinitionModel property)
   {
      var storage = property.Storage;

      var primaryKeyArgument = property.IsPrimaryKey ? ", isPrimaryKey: true" : string.Empty;
      var notNullArgument = !property.IsPrimaryKey && property.Nullability.IsNotNull ? ", isNotNull: true" : string.Empty;
      var trailingArguments = $"{primaryKeyArgument}{notNullArgument}";
      var columnArguments = $"x => x.{property.PropertyName}, {ToLiteral(property.ColumnName)}";

      if (storage.MappedAs is null)
         return $".Column({columnArguments}{trailingArguments})";

      var claim = $"{ColumnStorage.CLAIM_TYPE_FULL_NAME}.{storage.MappedAs}";

      if (storage.Conversion is not { } conversion)
         return $".Column({columnArguments}, {claim}{trailingArguments})";

      var nullability = property.TypeCanHoldNull ? "?" : string.Empty;
      var (toStored, fromStored) = Conversions(conversion, storage.ValueTypeName, nullability);

      return
         $".Column<{property.TypeName}, {conversion.StoredTypeName}{nullability}>({columnArguments}, {claim}, {toStored}, {fromStored}{trailingArguments})";
   }

   private static (string ToStored, string FromStored) Conversions(StorageConversion conversion, string valueTypeName, string nullability)
   {
      if (!conversion.IsEnumMemberName)
         return ($"static x => ({conversion.StoredTypeName}{nullability})x", $"static x => ({valueTypeName}{nullability})x");

      var parse = $"global::System.Enum.Parse<{valueTypeName}>(x, true)";

      return nullability.Length == 0
         ? ("static x => x.ToString()", $"static x => {parse}")
         : ("static x => x == null ? null : x.Value.ToString()", $"static x => x == null ? null : ({valueTypeName}?){parse}");
   }

   private static string RelationCall(ResolvedRelation relation)
   {
      var comparisons = relation.JoinedKeys.Select(x => $"x.{x.ThisKey.PropertyName} == y.{x.TargetKey.PropertyName}");

      if (relation.ConditionBodyText is { } condition)
         comparisons = comparisons.Append(condition);

      return $".Relation<{relation.TargetDataTypeName}>(x => x.{relation.PropertyName}, (x, y) => {string.Join(" && ", comparisons)})";
   }

   private static string ToLiteral(string value)
   {
      return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
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
