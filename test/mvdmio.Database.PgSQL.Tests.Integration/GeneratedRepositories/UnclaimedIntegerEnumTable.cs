using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

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
