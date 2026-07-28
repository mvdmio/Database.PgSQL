using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;
using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    Every query option this suite covers, applied to an entity whose key is two columns, plus the two constructs where
///    the key arity is visible in what reaches the database: <c>$select</c>, which appends every key column, and a
///    navigation path, whose join carries every key column.
/// </summary>
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
      // A key member is an ordinary filterable column; nothing about it being part of the key changes the predicate.
      (await CodesAsync(ApplyToProjects($"$filter=AccountId eq {SECOND_ACCOUNT}&$orderby=Code"))).Should().Equal("aurora", "vega");
   }

   [Fact]
   public async Task OrderBy_OverACompositeKeyEntity_SortsInTheDatabase()
   {
      var descending = ApplyToProjects("$orderby=Name desc");

      descending.RenderSql().Should().ContainAll("ORDER BY", "DESC");

      (await CodesAsync(descending)).Should().Equal("vega", "borealis", "aurora", "apollo");

      // Compound ordering over both key members, which is the ordering a stable page boundary is built on.
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

   /// <remarks>
   ///    Worth stating rather than leaving as a surprise: OData adds the key to every projection so the entity stays
   ///    addressable, and over a composite key that means <em>every</em> key column, not one. A narrowed projection is
   ///    therefore two columns wider than the client asked for.
   /// </remarks>
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

      // borealis matches, and vega has no tasks at all, which satisfies "all" vacuously as OData Part 2 specifies.
      (await CodesAsync(applied)).Should().Equal("borealis", "vega");
   }

   [Fact]
   public async Task Filter_ThroughANavigationProperty_StaysInsideTheTenantEvenWhenTheFarKeyMemberCollides()
   {
      // Both accounts have a task numbered 10. Reaching from a task to its project carries the tenancy column, so the
      // two never cross.
      (await CodesAsync(ApplyToProjects("$filter=Tasks/any(task: task/TaskId eq 10)&$orderby=Code")))
         .Should().Equal("apollo", "aurora");
   }

   /// <remarks>
   ///    Server-driven paging over a composite key. The token names one value per ordering property, and the predicate it
   ///    becomes is a lexicographic ladder: strictly greater on the first member, or equal on it and greater on the next.
   ///    That is what makes a page boundary stable when the first member repeats.
   /// </remarks>
   [Fact]
   public async Task SkipToken_OverACompositeKey_PagesByALexicographicLadderAcrossEveryKeyMember()
   {
      var borealisId = ProjectIds["borealis"];
      var applied = ApplyToProjects($"$orderby=AccountId,ProjectId&$skiptoken=AccountId-{FIRST_ACCOUNT},ProjectId-{borealisId}");
      var sql = applied.RenderSql();

      // The ladder itself, in the statement: a strict comparison on the first key member, and a further one on the second
      // for the rows where the first is equal.
      sql.Should().Contain("WHERE");
      sql.Should().MatchRegex(@"account_id\s*>");
      sql.Should().MatchRegex(@"project_id\s*>");
      sql.Should().MatchRegex(@"account_id\s*=");

      // Everything after (1, borealis) in that ordering, and nothing before it — borealis itself included.
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

   /// <summary>An equality on the same column across two table aliases, whichever aliases the provider chose.</summary>
   private static string CrossTableEquality(string columnName)
   {
      var column = Regex.Escape(columnName);
      var qualified = $@"(?:""[^""]+""|\w+)\.""?{column}""?";

      return $@"{qualified}\s*=\s*{qualified}";
   }
}
