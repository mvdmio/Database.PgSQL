using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    One half of a tenant-scoped pair whose primary keys are two columns and share the first of them, so that a relation
///    whose foreign key overlaps the declaring table's own key is exercised rather than assumed.
/// </summary>
/// <remarks>
///    A separate table set from the author-and-book one, which pins the single-column key path. The second key member is
///    database-generated, which is what makes a key that is part caller-supplied and part computed observable. Declared
///    in the RelationDefinition&lt;,&gt; form: pairs cover the same composite-key and generated-column-per-kind shapes
///    Key order used to, without a foreign-key/primary-key side for cardinality to work out.
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
