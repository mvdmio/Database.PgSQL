using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_tenancy_profiles")]
public partial class TenancyProfileTable
{
   [Column(Tenancy = true)]
   [PrimaryKey]
   public long AccountId { get; set; }

   public bool IsActive { get; set; }

   public string DisplayName { get; set; } = string.Empty;
}
