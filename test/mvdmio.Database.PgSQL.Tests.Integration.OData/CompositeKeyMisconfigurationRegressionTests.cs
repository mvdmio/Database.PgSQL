using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

public class CompositeKeyMisconfigurationRegressionTests : CompositeKeyConformanceTestBase
{
   public CompositeKeyMisconfigurationRegressionTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public void Expand_ToManyRowsOverACompositeKey_ComesBackEmptyWhenNullPropagationIsLeftAtItsDefault()
   {
      const string QUERY_STRING = "$expand=Tasks&$orderby=Code";

      var misconfigured = MisconfiguredProjects(QUERY_STRING).ProjectedRows();

      misconfigured.Select(x => x["Code"]).Should().Equal("apollo", "aurora", "borealis", "vega");
      misconfigured.Should().AllSatisfy(row => row.ExpandedMany(nameof(TenantProjectData.Tasks)).Should().BeEmpty());

      ApplyToProjects(QUERY_STRING).ProjectedRows()[0]
         .ExpandedMany(nameof(TenantProjectData.Tasks))
         .Select(x => x["Title"])
         .Should().BeEquivalentTo(new[] { "assemble", "launch" });
   }

   [Fact]
   public void Expand_ToManyRowsOverACompositeKey_SendsTheSameStatementEitherWay()
   {
      const string QUERY_STRING = "$expand=Tasks&$orderby=Code";

      MisconfiguredProjects(QUERY_STRING).RenderSql().Should().Be(ApplyToProjects(QUERY_STRING).RenderSql());
   }

   [Fact]
   public void Expand_ToOneRowOverACompositeKey_IsUnaffectedByTheNullPropagationDefault()
   {
      const string QUERY_STRING = "$expand=Project&$orderby=Title";

      var misconfigured = MisconfiguredTasks(QUERY_STRING).ProjectedRows();

      misconfigured[0].Expanded(nameof(TenantTaskData.Project))!["Code"].Should().Be("apollo");
      misconfigured.Select(x => x.Values).Should().BeEquivalentTo(ApplyToTasks(QUERY_STRING).ProjectedRows().Select(x => x.Values));
   }

   [Fact]
   public async Task Filter_WithAnAllQuantifierOverACompositeRelation_ReturnsTheWrongRowsWhenNullPropagationIsLeftAtItsDefault()
   {
      const string QUERY_STRING = "$filter=Tasks/all(task: task/Title eq 'survey')&$orderby=Code";

      var misconfigured = MisconfiguredProjects(QUERY_STRING);

      misconfigured.RenderSql().Should().ContainAll("NOT EXISTS(", "EXISTS(");

      (await CodesAsync(misconfigured)).Should().Equal("borealis");

      (await CodesAsync(ApplyToProjects(QUERY_STRING))).Should().Equal("borealis", "vega");
   }

   private AppliedQuery MisconfiguredProjects(string queryString)
   {
      return ODataQuery.Apply(Projects.Query(), queryString, ODataConfiguration.MisconfiguredQuerySettings, ODataConfiguration.CompositeModel);
   }

   private AppliedQuery MisconfiguredTasks(string queryString)
   {
      return ODataQuery.Apply(Tasks.Query(), queryString, ODataConfiguration.MisconfiguredQuerySettings, ODataConfiguration.CompositeModel);
   }
}
