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

   /// <remarks>
   ///    Emitted as a module initializer so the mappings are in place before any generated repository can be used,
   ///    without the library having to discover them by reflection. Relations are registered here too, in the same
   ///    callback as the entity's columns, so both ends of every relation are always registered together.
   /// </remarks>
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
            // The type arguments are stated because a property typed as a concrete list satisfies both Relation
            // overloads, which would make the call ambiguous if they were left to be inferred.
            builder.AppendLine($"            {RelationCall(relation)}");
         }

         builder.AppendLine("      );");
      }

      builder.AppendLine("   }");
   }

   /// <summary>
   ///    How one column is registered with the query surface, storage claim and all.
   /// </summary>
   /// <remarks>
   ///    The claim emitted here is read from the same <see cref="ColumnStorage" /> the parameter binding is emitted from,
   ///    which is what stops the two surfaces disagreeing about the column. Three shapes, picked by what the claim needs:
   ///    nothing stated at all, a claim alone, or a claim with the conversion that carries the value to it.
   ///    <para>
   ///       The type arguments on the converting shape are stated rather than inferred, for the same reason a relation's
   ///       are: the stored type is only inferable from a lambda body, and stating it makes the emitted call say which
   ///       conversion it means.
   ///    </para>
   /// </remarks>
   private static string ColumnCall(PropertyDefinitionModel property)
   {
      var storage = property.Storage;

      // A key member is already not-null through the key argument, which the builder itself acts on, so saying it twice
      // would only make the emitted call longer. Nullable needs no argument either: it is what the query surface assumes.
      var primaryKeyArgument = property.IsPrimaryKey ? ", isPrimaryKey: true" : string.Empty;
      var notNullArgument = !property.IsPrimaryKey && property.IsDeclaredNotNull ? ", isNotNull: true" : string.Empty;
      var trailingArguments = $"{primaryKeyArgument}{notNullArgument}";
      var columnArguments = $"x => x.{property.PropertyName}, {ToLiteral(property.ColumnName)}";

      if (storage.MappedAs is null)
         return $".Column({columnArguments}{trailingArguments})";

      var claim = $"{ColumnStorage.CLAIM_TYPE_FULL_NAME}.{storage.MappedAs}";

      if (storage.Conversion is not { } conversion)
         return $".Column({columnArguments}, {claim}{trailingArguments})";

      var nullability = property.IsNullable ? "?" : string.Empty;
      var (toStored, fromStored) = Conversions(conversion, storage.ValueTypeName, nullability);

      return
         $".Column<{property.TypeName}, {conversion.StoredTypeName}{nullability}>({columnArguments}, {claim}, {toStored}, {fromStored}{trailingArguments})";
   }

   /// <summary>The pair of lambdas carrying a value to what its column holds and back again.</summary>
   /// <remarks>
   ///    Both directions are decided in one place, off one reading of the conversion, because they are two halves of one
   ///    answer and a mismatched pair is a column that writes one representation and reads another.
   ///    <para>
   ///       A cast is its own inverse in shape. Text is not: an enum is parsed case-insensitively coming back, matching what
   ///       Dapper does natively for a text column with no handler registered, so a value differing in case from the member
   ///       name behaves the same on both surfaces.
   ///    </para>
   ///    <para>
   ///       Null is handled here as well as by the provider, which skips a conversion for a null by default. Written out
   ///       anyway, because the emitted lambdas have to type-check on their own and a <see cref="Nullable{T}" />'s own
   ///       <c>ToString</c> answers an empty string for a null rather than a null.
   ///    </para>
   /// </remarks>
   private static (string ToStored, string FromStored) Conversions(StorageConversion conversion, string valueTypeName, string nullability)
   {
      if (!conversion.IsEnumMemberName)
         return ($"static x => ({conversion.StoredTypeName}{nullability})x", $"static x => ({valueTypeName}{nullability})x");

      var parse = $"global::System.Enum.Parse<{valueTypeName}>(x, true)";

      return nullability.Length == 0
         ? ("static x => x.ToString()", $"static x => {parse}")
         : ("static x => x == null ? null : x.Value.ToString()", $"static x => x == null ? null : ({valueTypeName}?){parse}");
   }

   /// <summary>
   ///    How one relation is registered with the query surface.
   /// </summary>
   /// <remarks>
   ///    A relation joining one pair of columns keeps the key-based overload it has always used. A relation joining more
   ///    than one pair takes the predicate overload instead, which is not a preference: the key-based overloads carry a
   ///    single key each and their key type parameters are unconstrained, so an anonymous type or a tuple compiles there
   ///    and registers as one key named after its constructor, failing only at the first query. A predicate is checked
   ///    member by member at build time, so that shape is unreachable from here.
   /// </remarks>
   private static string RelationCall(ResolvedRelation relation)
   {
      if (!relation.IsComposite)
      {
         var only = relation.JoinedKeys[0];

         return
            $".Relation<{relation.TargetDataTypeName}, {only.ThisKey.TypeName}, {only.TargetKey.TypeName}>(x => x.{relation.PropertyName}, x => x.{only.ThisKey.PropertyName}, x => x.{only.TargetKey.PropertyName})";
      }

      var comparisons = relation.JoinedKeys.Select(x => $"x.{x.ThisKey.PropertyName} == y.{x.TargetKey.PropertyName}");

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
