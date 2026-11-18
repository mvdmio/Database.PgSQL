using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;
using System;
using System.Linq.Expressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

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
