using Microsoft.AspNetCore.OData.Query.Wrapper;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    One row of a projecting query, as the name-value pairs it produced. Needed because <c>$select</c>, <c>$expand</c>
///    and <c>$apply</c> project into OData's own wrapper types, which are internal to its assembly and so cannot be
///    named here.
/// </summary>
/// <remarks>
///    Unwrapping recurses, because an expanded value is itself a wrapper or a collection of them. The recursion and the
///    accessors live on one type deliberately: what a nested value is stored as and what a test asks for it as are then
///    the same decision, rather than a cast in one file betting on what another file produced.
/// </remarks>
public sealed class ProjectedRow
{
   private readonly IReadOnlyDictionary<string, object?> _values;

   /// <summary>The names this row carries, which is what a projection narrowed it to.</summary>
   public IEnumerable<string> Keys => _values.Keys;

   /// <summary>The pairs themselves, so that one whole row can be compared against another.</summary>
   public IReadOnlyDictionary<string, object?> Values => _values;

   /// <summary>The value projected under the given name.</summary>
   /// <param name="propertyName">The name the projection produced the value under.</param>
   public object? this[string propertyName] => Value(propertyName);

   private ProjectedRow(IReadOnlyDictionary<string, object?> values)
   {
      _values = values;
   }

   /// <summary>Unwraps one of OData's projection wrappers, and every wrapper nested inside it.</summary>
   /// <param name="row">The wrapper the query produced.</param>
   /// <returns>The row's values, with nested wrappers unwrapped into rows of their own.</returns>
   public static ProjectedRow From(object row)
   {
      return row switch
      {
         ISelectExpandWrapper selected => new ProjectedRow(selected.ToDictionary().ToDictionary(x => x.Key, x => Unwrap(x.Value), StringComparer.Ordinal)),
         DynamicTypeWrapper aggregated => new ProjectedRow(aggregated.Values.ToDictionary(x => x.Key, x => Unwrap(x.Value), StringComparer.Ordinal)),
         _ => throw new InvalidOperationException($"'{row.GetType()}' is not one of OData's projection wrappers.")
      };
   }

   /// <summary>The row an expanded to-one navigation property produced, or null when the expansion found nothing.</summary>
   /// <param name="propertyName">The navigation property the query string expanded.</param>
   public ProjectedRow? Expanded(string propertyName)
   {
      return Value(propertyName) switch
      {
         null => null,
         ProjectedRow nested => nested,
         var other => throw new InvalidOperationException($"'{propertyName}' projected '{other.GetType()}' rather than one expanded row.")
      };
   }

   /// <summary>The rows an expanded to-many navigation property produced.</summary>
   /// <param name="propertyName">The navigation property the query string expanded.</param>
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

         // Covariance covers whichever concrete collection OData chose, including its truncating one.
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
