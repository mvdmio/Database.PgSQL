using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    A tenancy column that is part of the primary key — the shape the driving multi-tenant schema in ADR 0009 has,
///    where every key already starts with the tenant. A separate table set from
///    <see cref="TenantProjectTable" />/<see cref="TenantTaskTable" />/<see cref="TenantLinkTable" />, whose current
///    generated shape <see cref="GeneratedRepositoryCompositeKeyTests" /> and the OData suite depend on.
/// </summary>
[Table("public.generated_tenancy_documents")]
public partial class TenancyDocumentTable
{
   [Column(Tenancy = true)]
   [PrimaryKey]
   public long AccountId { get; set; }

   [PrimaryKey]
   [Generated]
   public long DocumentId { get; set; }

   [Unique]
   public string Code { get; set; } = string.Empty;

   public string Title { get; set; } = string.Empty;

   public string Body { get; set; } = string.Empty;
}
