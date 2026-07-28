using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    <c>$expand</c> over the composite-key pair, in both cardinalities and two levels deep. The pair to
///    <see cref="ExpandConformanceTests" />, which covers the same constructs over single-column keys.
/// </summary>
/// <remarks>
///    Nothing else guards <c>$expand</c> against a composite key, and it is the construct with most to go wrong: OData
///    binds an expansion as a projection of its own and selects the navigation property inside it, so what makes the
///    projected member translatable is the provider-level association the generator registers — which is exactly the part
///    a composite key changes, from a pair of key expressions to a predicate.
/// </remarks>
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

      // The task numbered 10 under the other account reaches that account's project, not the first one's.
      rows[2].Expanded(nameof(TenantTaskData.Project))!["Code"].Should().Be("aurora");
   }

   [Fact]
   public void Expand_ToManyRowsOverACompositeKey_ReturnsTheRelatedRows()
   {
      var applied = ApplyToProjects("$expand=Tasks&$orderby=Code");

      // Like the single-key case, the related table is nowhere in the statement the query renders to.
      applied.RenderSql().Should().NotContain("odata_tenant_tasks");

      var rows = applied.ProjectedRows();

      rows.Select(x => x["Code"]).Should().Equal("apollo", "aurora", "borealis", "vega");
      rows[0].ExpandedMany(nameof(TenantProjectData.Tasks)).Select(x => x["Title"]).Should().BeEquivalentTo(new[] { "assemble", "launch" });
      rows[1].ExpandedMany(nameof(TenantProjectData.Tasks)).Select(x => x["Title"]).Should().Equal("observe");

      // The project with no tasks, so an empty expanded collection is observable rather than assumed.
      rows[3].ExpandedMany(nameof(TenantProjectData.Tasks)).Should().BeEmpty();
   }

   [Fact]
   public void Expand_ToManyRowsOverACompositeKey_ScopesTheRelatedRowsByEveryKeyColumn()
   {
      // Both accounts have a task numbered 10. If the detail query correlated on the project column alone the two would
      // still not cross, because project identifiers happen to be unique — so this asserts the tenancy column the other
      // way round: the second account's project gets its own task and none of the first account's.
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

      // A nested $select narrows to exactly what was asked for, so neither key column is added back.
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
