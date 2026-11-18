using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

public class QueryOptionConformanceTests : SampleConformanceTestBase
{
   public QueryOptionConformanceTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public async Task Filter_ReachesTheDatabaseAsAParameterizedWhereClause()
   {
      var applied = Apply("$filter=Tier eq 'gold'");
      var sql = applied.RenderSql();

      sql.Should().Contain("WHERE");
      sql.Should().Contain(".tier");

      sql.Should().NotContain("'gold'");

      (await NamesAsync(applied)).Should().BeEquivalentTo("alice", "carol");
   }

   [Fact]
   public async Task Filter_WithCombinedPredicates_ReachesTheDatabaseAsOneWhereClause()
   {
      var applied = Apply("$filter=Tier eq 'gold' and Rating gt 4 or Name eq 'dave'");

      applied.RenderSql().Should().Contain("WHERE");

      (await NamesAsync(applied)).Should().BeEquivalentTo("carol", "dave");
   }

   [Fact]
   public async Task OrderBy_SortsInTheDatabase()
   {
      var descending = Apply("$orderby=Name desc");

      descending.RenderSql().Should().ContainAll("ORDER BY", "DESC");

      (await NamesAsync(descending)).Should().Equal("dave", "carol", "bob", "alice");

      var compound = Apply("$orderby=Tier asc,Amount desc");

      (await NamesAsync(compound)).Should().Equal("carol", "alice", "bob", "dave");
   }

   [Fact]
   public async Task TopAndSkip_ReachTheDatabaseAsLimitAndOffset()
   {
      var applied = Apply("$orderby=Name&$top=2&$skip=1");

      applied.RenderSql().Should().ContainAll("LIMIT", "OFFSET");

      (await NamesAsync(applied)).Should().Equal("bob", "carol");
   }

   [Fact]
   public void Count_ReachesTheDatabaseAsAnAggregate()
   {
      var applied = Apply("$count=true");

      applied.TotalCount.Should().Be(4L);

      var countSql = applied.LastSql()!.ToUpperInvariant();
      countSql.Should().Contain("COUNT(");
      countSql.Should().NotContain("NAME");
   }

   [Fact]
   public void Count_WithAFilter_CountsOnlyTheMatchingRows()
   {
      Apply("$count=true&$filter=Tier eq 'gold'").TotalCount.Should().Be(2L);
   }

   [Fact]
   public void Select_NarrowsTheColumnsActuallyQueried()
   {
      var sql = Apply("$select=Name,Rating").RenderSql();

      sql.Should().ContainAll(".name", ".rating");
      sql.Should().NotContainAny(".nickname", ".amount", ".created_at", ".tier");

      sql.Should().Contain(".sample_id");
   }

   [Fact]
   public void Select_ReturnsOnlyTheSelectedValues()
   {
      var rows = Apply("$select=Name,Rating&$orderby=Name").ProjectedRows();

      rows.Should().HaveCount(4);
      rows[0].Keys.Should().BeEquivalentTo("Name", "Rating");
      rows[0]["Name"].Should().Be("alice");
      rows[0]["Rating"].Should().Be(3);
   }

   [Fact]
   public void Apply_WithGroupingAndAggregation_ReachesTheDatabaseAsGroupBy()
   {
      var applied = Apply("$apply=groupby((Tier), aggregate(Amount with sum as Total))");

      applied.RenderSql().Should().ContainAll("GROUP BY", "SUM(");

      var rows = applied.ProjectedRows();

      rows.Should().HaveCount(2);
      rows.Should().ContainSingle(x => Equals(x["Tier"], "gold") && Equals(x["Total"], 40.50m));
      rows.Should().ContainSingle(x => Equals(x["Tier"], "silver") && Equals(x["Total"], 26.00m));
   }

   [Fact]
   public void Apply_WithSeveralAggregates_ReachesTheDatabaseAsOneGroupedStatement()
   {
      var applied = Apply("$apply=groupby((Tier), aggregate($count as Rows, Rating with max as Highest))");

      applied.RenderSql().Should().ContainAll("GROUP BY", "COUNT(", "MAX(");

      var rows = applied.ProjectedRows();

      rows.Should().ContainSingle(x => Equals(x["Tier"], "gold") && Equals(x["Rows"], 2L) && Equals(x["Highest"], 8));
      rows.Should().ContainSingle(x => Equals(x["Tier"], "silver") && Equals(x["Rows"], 2L) && Equals(x["Highest"], 5));
   }

   [Fact]
   public void Apply_WithoutGrouping_AggregatesTheWholeTableInTheDatabase()
   {
      var applied = Apply("$apply=aggregate(Amount with sum as Total)");
      var sql = applied.RenderSql();

      sql.Should().Contain("SUM(");
      sql.Should().NotContain("GROUP BY");

      var rows = applied.ProjectedRows();

      rows.Should().ContainSingle();
      rows[0]["Total"].Should().Be(66.50m);
   }

   [Fact]
   public void Apply_WithAFilterAheadOfTheGrouping_GroupsOnlyTheMatchingRows()
   {
      var applied = Apply("$apply=filter(IsActive eq true)/groupby((Tier), aggregate($count as Rows))");

      applied.RenderSql().Should().ContainAll("WHERE", "GROUP BY");

      var rows = applied.ProjectedRows();

      rows.Should().ContainSingle();
      rows[0]["Tier"].Should().Be("gold");
      rows[0]["Rows"].Should().Be(2L);
   }
}
