using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    A second definition over <see cref="StorageClaimTable" />'s table, mapping its <c>integer</c> enum column without
///    claiming it. This is the shape a consumer has who was relying on the query surface's old default of storing an enum
///    as its underlying number, and it exists so that the change of default is demonstrated to break loudly rather than
///    described as breaking loudly.
/// </summary>
[Table("public.generated_storage_claims")]
public partial class UnclaimedIntegerEnumTable
{
   [PrimaryKey]
   [Generated]
   public long ClaimId { get; private set; }

   [Column("priority")]
   public WorkState Priority { get; set; }

   [Column("legacy_document")]
   public string LegacyDocument { get; set; } = string.Empty;
}
