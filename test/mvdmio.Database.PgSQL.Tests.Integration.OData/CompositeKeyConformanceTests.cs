using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;
using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

public class CompositeKeyConformanceTests : CompositeKeyConformanceTestBase
{
   public CompositeKeyConformanceTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public async Task Filter_OverACompositeKeyEntity_ReachesTheDatabaseAsAParameterizedWhereClause()
   {
      var applied = ApplyToProjects("$filter=Name eq 'Apollo'");
      var sql = applied.RenderSql();

      sql.Should().Contain("WHERE");
      sql.Should().Contain(".name");
      sql.Should().NotContain("'Apollo'");

      (await CodesAsync(applied)).Should().Equal("apollo");
   }

   [Fact]
   public async Task Filter_OnAKeyMember_ReachesTheDatabaseLikeAnyOtherColumn()
   {
      (await CodesAsync(ApplyToProjects($"$filter=AccountId eq {SECOND_ACCOUNT}&$orderby=Code"))).Should().Equal("aurora", "vega");
   }

   [Fact]
   public async Task OrderBy_OverACompositeKeyEntity_SortsInTheDatabase()
   {
      var descending = ApplyToProjects("$orderby=Name desc");

      descending.RenderSql().Should().ContainAll("ORDER BY", "DESC");

      (await CodesAsync(descending)).Should().Equal("vega", "borealis", "aurora", "apollo");

      (await CodesAsync(ApplyToProjects("$orderby=AccountId desc,ProjectId"))).Should().Equal("aurora", "vega", "apollo", "borealis");
   }

   [Fact]
   public async Task TopAndSkip_OverACompositeKeyEntity_ReachTheDatabaseAsLimitAndOffset()
   {
      var applied = ApplyToProjects("$orderby=Code&$top=2&$skip=1");

      applied.RenderSql().Should().ContainAll("LIMIT", "OFFSET");

      (await CodesAsync(applied)).Should().Equal("aurora", "borealis");
   }

   [Fact]
   public void Count_OverACompositeKeyEntity_ReachesTheDatabaseAsAnAggregate()
   {
      var applied = ApplyToProjects("$count=true");

      applied.TotalCount.Should().Be(4L);

      var countSql = applied.LastSql()!.ToUpperInvariant();
      countSql.Should().Contain("COUNT(");
      countSql.Should().NotContain("CODE");

      ApplyToProjects($"$count=true&$filter=AccountId eq {FIRST_ACCOUNT}").TotalCount.Should().Be(2L);
   }

   [Fact]
   public void Select_OverACompositeKeyEntity_AppendsEveryKeyColumn()
   {
      var sql = ApplyToProjects("$select=Code").RenderSql();

      sql.Should().Contain(".code");
      sql.Should().NotContain(".name");
      sql.Should().ContainAll(".account_id", ".project_id");
   }

   [Fact]
   public void Select_OverACompositeKeyEntity_ReturnsOnlyTheSelectedValues()
   {
      var rows = ApplyToProjects("$select=Code,Name&$orderby=Code").ProjectedRows();

      rows.Should().HaveCount(4);
      rows[0].Keys.Should().BeEquivalentTo("Code", "Name");
      rows[0]["Code"].Should().Be("apollo");
      rows[0]["Name"].Should().Be("Apollo");
   }

   [Fact]
   public async Task Filter_ThroughAToOneNavigationPropertyBetweenCompositeKeyEntities_JoinsOnEveryKeyColumn()
   {
      var applied = ApplyToTasks("$filter=Project/Name eq 'Apollo'&$orderby=TaskId");
      var sql = applied.RenderSql();

      sql.Should().ContainAll("LEFT JOIN", "odata_tenant_projects", "WHERE");
      sql.Should().MatchRegex(CrossTableEquality("account_id"));
      sql.Should().MatchRegex(CrossTableEquality("project_id"));

      (await TitlesAsync(applied)).Should().Equal("assemble", "launch");
   }

   [Fact]
   public async Task Filter_ThroughAToManyNavigationPropertyBetweenCompositeKeyEntities_CorrelatesOnEveryKeyColumn()
   {
      var applied = ApplyToProjects("$filter=Tasks/any(task: task/Title eq 'launch')");
      var sql = applied.RenderSql();

      sql.Should().ContainAll("EXISTS(", "odata_tenant_tasks");
      sql.Should().MatchRegex(CrossTableEquality("account_id"));
      sql.Should().MatchRegex(CrossTableEquality("project_id"));

      (await CodesAsync(applied)).Should().Equal("apollo");
   }

   [Fact]
   public async Task Filter_WithAnAllQuantifierOverACompositeRelation_ReachesTheDatabaseAsANegatedExistsSubquery()
   {
      var applied = ApplyToProjects("$filter=Tasks/all(task: task/Title eq 'survey')&$orderby=Code");

      applied.RenderSql().Should().ContainAll("NOT EXISTS(", "odata_tenant_tasks");

      (await CodesAsync(applied)).Should().Equal("borealis", "vega");
   }

   [Fact]
   public async Task Filter_ThroughANavigationProperty_StaysInsideTheTenantEvenWhenTheFarKeyMemberCollides()
   {
      (await CodesAsync(ApplyToProjects("$filter=Tasks/any(task: task/TaskId eq 10)&$orderby=Code")))
         .Should().Equal("apollo", "aurora");
   }

   [Fact]
   public async Task SkipToken_OverACompositeKey_PagesByALexicographicLadderAcrossEveryKeyMember()
   {
      var borealisId = ProjectIds["borealis"];
      var applied = ApplyToProjects($"$orderby=AccountId,ProjectId&$skiptoken=AccountId-{FIRST_ACCOUNT},ProjectId-{borealisId}");
      var sql = applied.RenderSql();

      sql.Should().Contain("WHERE");
      sql.Should().MatchRegex(@"account_id\s*>");
      sql.Should().MatchRegex(@"project_id\s*>");
      sql.Should().MatchRegex(@"account_id\s*=");

      (await CodesAsync(applied)).Should().Equal("aurora", "vega");
   }

   [Fact]
   public async Task SkipToken_OverACompositeKey_CombinesWithTopToPageThroughTheWholeSet()
   {
      var firstPage = ApplyToProjects("$orderby=AccountId,ProjectId&$top=2");

      (await CodesAsync(firstPage)).Should().Equal("apollo", "borealis");

      var secondPage = ApplyToProjects(
         $"$orderby=AccountId,ProjectId&$top=2&$skiptoken=AccountId-{FIRST_ACCOUNT},ProjectId-{ProjectIds["borealis"]}"
      );

      (await CodesAsync(secondPage)).Should().Equal("aurora", "vega");
   }

   private static string CrossTableEquality(string columnName)
   {
      var column = Regex.Escape(columnName);
      var qualified = $@"(?:""[^""]+""|\w+)\.""?{column}""?";

      return $@"{qualified}\s*=\s*{qualified}";
   }
}
