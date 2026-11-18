using Microsoft.AspNetCore.OData.Query.Wrapper;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

public sealed class ProjectedRow
{
   private readonly IReadOnlyDictionary<string, object?> _values;

   public IEnumerable<string> Keys => _values.Keys;

   public IReadOnlyDictionary<string, object?> Values => _values;

   public object? this[string propertyName] => Value(propertyName);

   private ProjectedRow(IReadOnlyDictionary<string, object?> values)
   {
      _values = values;
   }

   public static ProjectedRow From(object row)
   {
      return row switch
      {
         ISelectExpandWrapper selected => new ProjectedRow(selected.ToDictionary().ToDictionary(x => x.Key, x => Unwrap(x.Value), StringComparer.Ordinal)),
         DynamicTypeWrapper aggregated => new ProjectedRow(aggregated.Values.ToDictionary(x => x.Key, x => Unwrap(x.Value), StringComparer.Ordinal)),
         _ => throw new InvalidOperationException($"'{row.GetType()}' is not one of OData's projection wrappers.")
      };
   }

   public ProjectedRow? Expanded(string propertyName)
   {
      return Value(propertyName) switch
      {
         null => null,
         ProjectedRow nested => nested,
         var other => throw new InvalidOperationException($"'{propertyName}' projected '{other.GetType()}' rather than one expanded row.")
      };
   }

   public IReadOnlyList<ProjectedRow> ExpandedMany(string propertyName)
   {
      return Value(propertyName) switch
      {
         null => [],
         IReadOnlyList<ProjectedRow> nested => nested,
         var other => throw new InvalidOperationException($"'{propertyName}' projected '{other.GetType()}' rather than a collection of expanded rows.")
      };
   }

   private static object? Unwrap(object? value)
   {
      return value switch
      {
         ISelectExpandWrapper nested => From(nested),

         IEnumerable<ISelectExpandWrapper> nested => nested.Select(From).ToList(),
         _ => value
      };
   }

   private object? Value(string propertyName)
   {
      if (!_values.TryGetValue(propertyName, out var value))
         throw new InvalidOperationException($"'{propertyName}' is not one of the projected values: {string.Join(", ", _values.Keys)}.");

      return value;
   }
}
