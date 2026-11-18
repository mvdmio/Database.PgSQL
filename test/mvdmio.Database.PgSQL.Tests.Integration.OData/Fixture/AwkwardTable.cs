using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

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

   public sbyte SignedOffset { get; set; }

   public DateTime OccurredAt { get; set; }
}
