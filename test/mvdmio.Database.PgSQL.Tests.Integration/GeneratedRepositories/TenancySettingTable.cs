using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    A tenancy column that sits outside a surrogate primary key — the common multi-tenant shape ADR 0009 also has to
///    cover, where the key alone does not already scope every row to its tenant.
/// </summary>
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
