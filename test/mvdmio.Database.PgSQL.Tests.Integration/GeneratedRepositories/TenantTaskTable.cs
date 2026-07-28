using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    The other half of the tenant-scoped pair. Its whole key is caller-supplied, so the pair covers both a fully supplied
///    composite key and a part-generated one, and its relation to a project is keyed on the tenancy column it shares with
///    its own key plus a column that is in no key at all.
/// </summary>
[Table("public.generated_tenant_tasks")]
public partial class TenantTaskTable
{
   [PrimaryKey]
   public long AccountId { get; set; }

   [PrimaryKey]
   public long TaskId { get; set; }

   public long ProjectId { get; set; }

   public string Title { get; set; } = string.Empty;

   [Relation(nameof(AccountId), nameof(ProjectId))]
   public TenantProjectTable? Project { get; set; }
}
