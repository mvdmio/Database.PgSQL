using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    A junction whose primary key is four columns, carrying a stored generated column that is non-null only for its own
///    kind — which is how a junction polymorphic across many tables is resolved.
/// </summary>
/// <remarks>
///    The library learns nothing about polymorphism: <see cref="ProjectRef" /> is an ordinary mapped column that happens
///    to be database-computed, and the relation declared against it is an ordinary relation. One kind is therefore the
///    proof for all of them — declaring a second would test the shape of a consumer's schema rather than this library.
///    It is also the nullable-foreign-key case against a non-nullable key member, which is what a per-kind column always
///    is.
/// </remarks>
[Table("public.generated_tenant_links")]
public partial class TenantLinkTable
{
   [PrimaryKey]
   public long AccountId { get; set; }

   [PrimaryKey]
   public long LinkId { get; set; }

   [PrimaryKey]
   public string Kind { get; set; } = string.Empty;

   [PrimaryKey]
   public int Ordinal { get; set; }

   public long TargetId { get; set; }

   /// <summary>Equal to <see cref="TargetId" /> when the kind is <c>project</c>, and null for every other kind.</summary>
   [Generated]
   public long? ProjectRef { get; set; }

   [Relation(nameof(AccountId), nameof(ProjectRef))]
   public TenantProjectTable? Project { get; set; }
}
