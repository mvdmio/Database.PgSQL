using mvdmio.Database.PgSQL.Attributes;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

[Table("public.odata_samples")]
public partial class SampleTable
{
   [PrimaryKey]
   [Generated]
   public long SampleId { get; set; }

   [Unique]
   public string Name { get; set; } = string.Empty;

   public string? Nickname { get; set; }

   public int Rating { get; set; }

   public int? Bonus { get; set; }

   public decimal Amount { get; set; }

   public DateTimeOffset CreatedAt { get; set; }

   public bool IsActive { get; set; }

   [Column(StoredAs = NpgsqlDbType.Integer)]
   public SampleCategory Category { get; set; }

   public Guid PublicId { get; set; }

   public string Tier { get; set; } = string.Empty;
}
