using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;
using System;
using System.Linq.Expressions;

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

   /// <summary>
   ///    Reaches <see cref="TenancyProfileTable" />, a per-tenant singleton whose whole primary key is the tenancy
   ///    column — pairing that one column alone already claims uniqueness, and the Relation condition narrows further
   ///    to only an active profile.
   /// </summary>
   private ProfileRelation? Profile { get; set; }

   private class ProfileRelation : RelationDefinition<TenancyDocumentTable, TenancyProfileTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AccountId, y => y.AccountId),
      ];

      public override Expression<Func<TenancyDocumentTable, TenancyProfileTable, bool>> Condition
         => (document, profile) => profile.IsActive;
   }
}
