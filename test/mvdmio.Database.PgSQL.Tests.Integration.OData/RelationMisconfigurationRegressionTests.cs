using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

public class RelationMisconfigurationRegressionTests : RelationConformanceTestBase
{
   public RelationMisconfigurationRegressionTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public void Expand_ToManyRows_ComesBackEmptyWhenNullPropagationIsLeftAtItsDefault()
   {
      const string QUERY_STRING = "$expand=Books&$orderby=Name";

      var misconfigured = MisconfiguredAuthors(QUERY_STRING).ProjectedRows();

      misconfigured.Select(x => x["Name"]).Should().Equal("gaiman", "lewis", "pratchett", "tolkien");
      misconfigured.Should().AllSatisfy(row => row.ExpandedMany(nameof(AuthorData.Books)).Should().BeEmpty());

      var configured = ApplyToAuthors(QUERY_STRING).ProjectedRows();

      configured[3].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("hobbit", "silmarillion");
   }

   [Fact]
   public void Expand_ToManyRows_SendsTheSameStatementEitherWay()
   {
      const string QUERY_STRING = "$expand=Books&$orderby=Name";

      MisconfiguredAuthors(QUERY_STRING).RenderSql().Should().Be(ApplyToAuthors(QUERY_STRING).RenderSql());
   }

   [Fact]
   public void Expand_ToOneRow_IsUnaffectedByTheNullPropagationDefault()
   {
      const string QUERY_STRING = "$expand=Author&$orderby=Title";

      var misconfigured = MisconfiguredBooks(QUERY_STRING).ProjectedRows();

      misconfigured[0].Expanded(nameof(BookData.Author))!["Name"].Should().Be("tolkien");
      misconfigured[2].Expanded(nameof(BookData.Author)).Should().BeNull();

      misconfigured.Select(x => x.Values).Should().BeEquivalentTo(ApplyToBooks(QUERY_STRING).ProjectedRows().Select(x => x.Values));
   }

   [Fact]
   public async Task Filter_WithAnAllQuantifier_ReturnsTheWrongRowsWhenNullPropagationIsLeftAtItsDefault()
   {
      const string QUERY_STRING = "$filter=Books/all(book: book/Title eq 'hobbit')&$orderby=Name";

      var misconfigured = MisconfiguredAuthors(QUERY_STRING);

      misconfigured.RenderSql().Should().ContainAll("NOT EXISTS(", "EXISTS(");

      (await NamesAsync(misconfigured)).Should().BeEmpty();

      (await NamesAsync(ApplyToAuthors(QUERY_STRING))).Should().Equal("gaiman", "pratchett");
   }

   private AppliedQuery MisconfiguredAuthors(string queryString)
   {
      return ODataQuery.Apply(Authors.Query(), queryString, ODataConfiguration.MisconfiguredQuerySettings, ODataConfiguration.RelationModel);
   }

   private AppliedQuery MisconfiguredBooks(string queryString)
   {
      return ODataQuery.Apply(Books.Query(), queryString, ODataConfiguration.MisconfiguredQuerySettings, ODataConfiguration.RelationModel);
   }
}
