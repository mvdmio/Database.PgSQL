using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_tenancy_settings")]
public partial class TenancySettingTable
{
   [PrimaryKey]
   [Generated]
   public long SettingId { get; set; }

   [Column(Tenancy = true)]
   public long AccountId { get; set; }

   [Unique]
   public string Code { get; set; } = string.Empty;

   public string Label { get; set; } = string.Empty;

   public string Value { get; set; } = string.Empty;
}
