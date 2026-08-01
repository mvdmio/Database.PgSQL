using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    A per-tenant singleton whose whole primary key is the tenancy column — the shape the Settled section of the
///    relation-definitions spec carves out: reachable by pairing that one column plus a Relation condition, since the
///    tenancy column being the whole primary key already makes the pairing unique on its own.
/// </summary>
[Table("public.generated_tenancy_profiles")]
public partial class TenancyProfileTable
{
   [Column(Tenancy = true)]
   [PrimaryKey]
   public long AccountId { get; set; }

   public bool IsActive { get; set; }

   public string DisplayName { get; set; } = string.Empty;
}
