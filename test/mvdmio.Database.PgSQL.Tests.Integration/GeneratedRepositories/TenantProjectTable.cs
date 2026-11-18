using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

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

   private PrimaryTaskRelation? PrimaryTask { get; set; }

   private List<TasksRelation> Tasks { get; set; } = [];

   private List<LinksRelation> Links { get; set; } = [];

   private class PrimaryTaskRelation : RelationDefinition<TenantProjectTable, TenantTaskTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AccountId, y => y.AccountId),
         Key(x => x.PrimaryTaskId, y => y.TaskId),
      ];
   }

   private class TasksRelation : RelationDefinition<TenantProjectTable, TenantTaskTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AccountId, y => y.AccountId),
         Key(x => x.ProjectId, y => y.ProjectId),
      ];
   }

   private class LinksRelation : RelationDefinition<TenantProjectTable, TenantLinkTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AccountId, y => y.AccountId),
         Key(x => x.ProjectId, y => y.ProjectRef),
      ];
   }
}
