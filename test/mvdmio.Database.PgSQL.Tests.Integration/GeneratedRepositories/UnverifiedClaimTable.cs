using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_unverified_claims")]
public partial class UnverifiedClaimTable
{
   [PrimaryKey]
   public long ClaimId { get; set; }

   public string Label { get; set; } = string.Empty;

   [Column(Null = true)]
   public string Note { get; set; } = string.Empty;
}
