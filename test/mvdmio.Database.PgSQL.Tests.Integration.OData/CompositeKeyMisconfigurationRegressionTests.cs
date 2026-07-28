using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    The null-propagation symptoms that need a relation to see, over a composite key rather than a single-column one. The
///    third member of the family alongside <see cref="MisconfigurationRegressionTests" /> and
///    <see cref="RelationMisconfigurationRegressionTests" />.
/// </summary>
/// <remarks>
///    The one setting a consumer must get right does not become discoverable by being documented once; it stays
///    discoverable by being guarded on every key shape the library admits. Like its pair, each test runs the same query
///    string twice — once with the mandated settings and once with
///    <see cref="ODataConfiguration.MisconfiguredQuerySettings" />, which leaves the setting untouched rather than forcing
///    it, so what they observe is what OData's namespace matching actually decides for this provider.
/// </remarks>
public class CompositeKeyMisconfigurationRegressionTests : CompositeKeyConformanceTestBase
{
   public CompositeKeyMisconfigurationRegressionTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public void Expand_ToManyRowsOverACompositeKey_ComesBackEmptyWhenNullPropagationIsLeftAtItsDefault()
   {
      const string queryString = "$expand=Tasks&$orderby=Code";

      var misconfigured = MisconfiguredProjects(queryString).ProjectedRows();

      // Every project, in the right order, with the right scalar values — and not one task.
      misconfigured.Select(x => x["Code"]).Should().Equal("apollo", "aurora", "borealis", "vega");
      misconfigured.Should().AllSatisfy(row => row.ExpandedMany(nameof(TenantProjectData.Tasks)).Should().BeEmpty());

      ApplyToProjects(queryString).ProjectedRows()[0]
         .ExpandedMany(nameof(TenantProjectData.Tasks))
         .Select(x => x["Title"])
         .Should().BeEquivalentTo(new[] { "assemble", "launch" });
   }

   [Fact]
   public void Expand_ToManyRowsOverACompositeKey_SendsTheSameStatementEitherWay()
   {
      // Why it is undetectable from either side: the query surface composed exactly the same statement, so there is no
      // failure to catch. The rewriting happens above it, in the projection OData binds the expansion as.
      const string queryString = "$expand=Tasks&$orderby=Code";

      MisconfiguredProjects(queryString).RenderSql().Should().Be(ApplyToProjects(queryString).RenderSql());
   }

   [Fact]
   public void Expand_ToOneRowOverACompositeKey_IsUnaffectedByTheNullPropagationDefault()
   {
      const string queryString = "$expand=Project&$orderby=Title";

      var misconfigured = MisconfiguredTasks(queryString).ProjectedRows();

      misconfigured[0].Expanded(nameof(TenantTaskData.Project))!["Code"].Should().Be("apollo");
      misconfigured.Select(x => x.Values).Should().BeEquivalentTo(ApplyToTasks(queryString).ProjectedRows().Select(x => x.Values));
   }

   [Fact]
   public async Task Filter_WithAnAllQuantifierOverACompositeRelation_ReturnsTheWrongRowsWhenNullPropagationIsLeftAtItsDefault()
   {
      const string queryString = "$filter=Tasks/all(task: task/Title eq 'survey')&$orderby=Code";

      var misconfigured = MisconfiguredProjects(queryString);

      // OData adds an EXISTS on top of its own NOT EXISTS, so a project with no tasks no longer qualifies — and an empty
      // collection satisfying "all" vacuously is the specified behaviour.
      misconfigured.RenderSql().Should().ContainAll("NOT EXISTS(", "EXISTS(");

      (await CodesAsync(misconfigured)).Should().Equal("borealis");

      // Not a translation failure and not an error: a different, wrong row set.
      (await CodesAsync(ApplyToProjects(queryString))).Should().Equal("borealis", "vega");
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
