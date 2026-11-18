using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

[Table("public.odata_tenant_tasks")]
public partial class TenantTaskTable
{
   [PrimaryKey]
   public long AccountId { get; set; }

   [PrimaryKey]
   public long TaskId { get; set; }

   public long ProjectId { get; set; }

   [Unique]
   public string Title { get; set; } = string.Empty;

   private ProjectRelation? Project { get; set; }

   private class ProjectRelation : RelationDefinition<TenantTaskTable, TenantProjectTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AccountId, y => y.AccountId),
         Key(x => x.ProjectId, y => y.ProjectId),
      ];
   }
}
