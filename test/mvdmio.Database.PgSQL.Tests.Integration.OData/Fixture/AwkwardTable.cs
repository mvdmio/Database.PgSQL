using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    The awkward-types entity: every property type a table definition admits whose behaviour in an OData model is not an
///    equivalence. The EDM primitive set has no character type and offers only an offset-bearing instant rather than a
///    plain one, so each of these is a convention at best.
/// </summary>
/// <remarks>
///    Kept off <see cref="SampleTable" /> deliberately. The failure mode is unknown, and if the convention model
///    builder rejects one of these types outright, having it on the conformance entity would break every test in the
///    suite instead of the one test asking the question.
/// </remarks>
[Table("public.odata_awkward")]
public partial class AwkwardTable
{
   [PrimaryKey]
   [Generated]
   public long AwkwardId { get; set; }

   public Uri? HomePage { get; set; }

   public Dictionary<string, string>? Metadata { get; set; }

   public DateOnly BirthDate { get; set; }

   public TimeOnly WakeTime { get; set; }

   public TimeSpan Duration { get; set; }

   public byte[]? Payload { get; set; }

   public char Initial { get; set; }

   /// <summary>
   ///    Writable without a claim: a signed byte is widened to a small integer automatically, because the only thing that
   ///    ever broke was the <c>DbType</c> Dapper inferred for it. The unsigned widths that used to sit alongside it are
   ///    gone — <c>PGSQL0023</c> refuses them now, since no PostgreSQL type accepts one.
   /// </summary>
   public sbyte SignedOffset { get; set; }

   /// <summary>Plain, offset-free. EDM has no equivalent, so whatever the model builder does with it is a convention.</summary>
   public DateTime OccurredAt { get; set; }
}
