using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    The conformance entity. Every property type here has a direct EDM primitive equivalent, so the model built from
///    it must build cleanly — if it does not, that is a bug in this suite rather than a finding. The column set is
///    chosen to cover the surface under test: text and nullable text for the string functions, a signed-integral and a
///    decimal column for the arithmetic functions and for <c>$apply</c> aggregation, an offset-bearing instant for the
///    date functions, a boolean, an enum, a unique identifier, and a low-cardinality column to group by.
/// </summary>
[Table("public.odata_samples")]
public partial class SampleTable
{
   [PrimaryKey]
   [Generated]
   public long SampleId { get; set; }

   [Unique]
   public string Name { get; set; } = string.Empty;

   /// <summary>Nullable so that null-comparison semantics are covered rather than assumed.</summary>
   public string? Nickname { get; set; }

   public int Rating { get; set; }

   /// <summary>Nullable so that inequality against a null-bearing numeric column is covered too.</summary>
   public int? Bonus { get; set; }

   public decimal Amount { get; set; }

   public DateTimeOffset CreatedAt { get; set; }

   public bool IsActive { get; set; }

   public SampleCategory Category { get; set; }

   public Guid PublicId { get; set; }

   /// <summary>Low cardinality, so <c>$apply</c> grouping produces more than one row per group.</summary>
   public string Tier { get; set; } = string.Empty;
}
