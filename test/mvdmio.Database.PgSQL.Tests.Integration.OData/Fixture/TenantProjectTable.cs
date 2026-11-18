using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

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

   private List<TasksRelation> Tasks { get; set; } = [];

   private class TasksRelation : RelationDefinition<TenantProjectTable, TenantTaskTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AccountId, y => y.AccountId),
         Key(x => x.ProjectId, y => y.ProjectId),
      ];
   }
}
