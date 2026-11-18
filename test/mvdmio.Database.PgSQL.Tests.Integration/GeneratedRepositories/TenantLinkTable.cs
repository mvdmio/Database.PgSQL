using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

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

   [Generated]
   public long? ProjectRef { get; set; }

   private ProjectRelation? Project { get; set; }

   private class ProjectRelation : RelationDefinition<TenantLinkTable, TenantProjectTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AccountId, y => y.AccountId),
         Key(x => x.ProjectRef, y => y.ProjectId),
      ];
   }
}
