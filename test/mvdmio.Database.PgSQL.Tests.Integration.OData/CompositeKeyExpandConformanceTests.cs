using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

public class CompositeKeyExpandConformanceTests : CompositeKeyConformanceTestBase
{
   public CompositeKeyExpandConformanceTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public void Expand_ToOneRowOverACompositeKey_FoldsIntoTheQueryAsAnOuterJoin()
   {
      var applied = ApplyToTasks("$expand=Project&$orderby=Title");
      var sql = applied.RenderSql();

      sql.Should().ContainAll("LEFT JOIN", "odata_tenant_projects");
      sql.Should().Contain(".code");

      var rows = applied.ProjectedRows();

      rows.Select(x => x["Title"]).Should().Equal("assemble", "launch", "observe", "survey");
      rows[0].Expanded(nameof(TenantTaskData.Project))!["Code"].Should().Be("apollo");

      rows[2].Expanded(nameof(TenantTaskData.Project))!["Code"].Should().Be("aurora");
   }

   [Fact]
   public void Expand_ToManyRowsOverACompositeKey_ReturnsTheRelatedRows()
   {
      var applied = ApplyToProjects("$expand=Tasks&$orderby=Code");

      applied.RenderSql().Should().NotContain("odata_tenant_tasks");

      var rows = applied.ProjectedRows();

      rows.Select(x => x["Code"]).Should().Equal("apollo", "aurora", "borealis", "vega");
      rows[0].ExpandedMany(nameof(TenantProjectData.Tasks)).Select(x => x["Title"]).Should().BeEquivalentTo(new[] { "assemble", "launch" });
      rows[1].ExpandedMany(nameof(TenantProjectData.Tasks)).Select(x => x["Title"]).Should().Equal("observe");

      rows[3].ExpandedMany(nameof(TenantProjectData.Tasks)).Should().BeEmpty();
   }

   [Fact]
   public void Expand_ToManyRowsOverACompositeKey_ScopesTheRelatedRowsByEveryKeyColumn()
   {
      var rows = ApplyToProjects($"$filter=AccountId eq {SECOND_ACCOUNT}&$expand=Tasks&$orderby=Code").ProjectedRows();

      rows.Select(x => x["Code"]).Should().Equal("aurora", "vega");
      rows[0].ExpandedMany(nameof(TenantProjectData.Tasks)).Select(x => x["TaskId"]).Should().Equal(10L);
      rows[1].ExpandedMany(nameof(TenantProjectData.Tasks)).Should().BeEmpty();
   }

   [Fact]
   public void Expand_NestedTwoLevelsDeepOverCompositeKeys_ReturnsTheRelatedRowsOfTheRelatedRows()
   {
      var rows = ApplyToProjects("$expand=Tasks($expand=Project)&$orderby=Code").ProjectedRows();

      var apollosTasks = rows[0].ExpandedMany(nameof(TenantProjectData.Tasks));

      apollosTasks.Select(x => x["Title"]).Should().BeEquivalentTo(new[] { "assemble", "launch" });
      apollosTasks.Should().AllSatisfy(task => task.Expanded(nameof(TenantTaskData.Project))!["Code"].Should().Be("apollo"));
   }

   [Fact]
   public void Expand_WithANestedFilterOverACompositeKey_NarrowsTheRelatedRows()
   {
      var rows = ApplyToProjects("$expand=Tasks($filter=Title eq 'launch')&$orderby=Code").ProjectedRows();

      rows.Select(x => x["Code"]).Should().Equal("apollo", "aurora", "borealis", "vega");
      rows[0].ExpandedMany(nameof(TenantProjectData.Tasks)).Select(x => x["Title"]).Should().Equal("launch");
      rows[2].ExpandedMany(nameof(TenantProjectData.Tasks)).Should().BeEmpty();
   }

   [Fact]
   public void Expand_WithANestedSelectOverACompositeKey_NarrowsTheRelatedColumns()
   {
      var rows = ApplyToProjects("$expand=Tasks($select=Title)&$orderby=Code").ProjectedRows();
      var tasks = rows[2].ExpandedMany(nameof(TenantProjectData.Tasks));

      tasks.Select(x => x["Title"]).Should().Equal("survey");

      tasks[0].Keys.Should().Equal("Title");
   }

   [Fact]
   public void Expand_WithANestedCountOverACompositeKey_ReachesTheDatabaseAsACorrelatedSubquery()
   {
      var applied = ApplyToProjects("$expand=Tasks($count=true)&$orderby=Code");

      applied.RenderSql().Should().ContainAll("COUNT(", "odata_tenant_tasks");

      var rows = applied.ProjectedRows();

      rows[0].ExpandedMany(nameof(TenantProjectData.Tasks)).Should().HaveCount(2);
      rows[3].ExpandedMany(nameof(TenantProjectData.Tasks)).Should().BeEmpty();
   }
}
