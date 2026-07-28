using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    One half of a tenant-scoped pair whose primary keys are two columns and share the first of them, so that a relation
///    whose foreign key overlaps the declaring table's own key is exercised rather than assumed.
/// </summary>
/// <remarks>
///    A separate table set from the author-and-book one, which pins the single-column key path. The second key member is
///    database-generated, which is what makes a key that is part caller-supplied and part computed observable.
/// </remarks>
[Table("public.generated_tenant_projects")]
public partial class TenantProjectTable
{
   [PrimaryKey]
   public long AccountId { get; set; }

   [PrimaryKey]
   [Generated]
   public long ProjectId { get; set; }

   [Unique]
   public string Code { get; set; } = string.Empty;

   public string Name { get; set; } = string.Empty;

   public long? PrimaryTaskId { get; set; }

   /// <summary>
   ///    Reaches a task by the tenancy column plus a task identifier that repeats across accounts, so the account column
   ///    is what tells two candidate rows apart rather than being redundant.
   /// </summary>
   [Relation(nameof(AccountId), nameof(PrimaryTaskId))]
   public TenantTaskTable? PrimaryTask { get; set; }

   [Relation(nameof(TenantTaskTable.AccountId), nameof(TenantTaskTable.ProjectId))]
   public List<TenantTaskTable> Tasks { get; set; } = [];

   [Relation(nameof(TenantLinkTable.AccountId), nameof(TenantLinkTable.ProjectRef))]
   public List<TenantLinkTable> Links { get; set; } = [];
}
