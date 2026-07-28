using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    One half of the composite-key pair the front-end is driven over. Its key is two columns, the second of them
///    database-generated, so a query option applied to it has to cope with a key that is neither single nor wholly
///    caller-supplied.
/// </summary>
/// <remarks>
///    Alongside <c>AuthorTable</c> and <c>BookTable</c> rather than replacing them: those pin the single-column key path,
///    and results are already asserted against them. Deliberately shaped like the main integration suite's tenant tables,
///    so the two suites can be read against each other.
/// </remarks>
[Table("public.odata_tenant_projects")]
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

   [Relation(nameof(TenantTaskTable.AccountId), nameof(TenantTaskTable.ProjectId))]
   public List<TenantTaskTable> Tasks { get; set; } = [];
}
