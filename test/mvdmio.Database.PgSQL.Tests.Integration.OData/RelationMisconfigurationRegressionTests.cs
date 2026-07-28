using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    The two silently-wrong-result symptoms of leaving the null-propagation setting at its default that need a relation
///    model to demonstrate: an expanded collection comes back empty, and a collection <c>all()</c> returns the wrong
///    rows. The pair to <see cref="MisconfigurationRegressionTests" />, which covers the symptoms visible on a single
///    table and is bound to the conformance fixture.
/// </summary>
/// <remarks>
///    <para>
///       These are the claims the packed library README, ADR 0005 and this project's README all made without evidence.
///       They hold, and this is where they are now pinned.
///    </para>
///    <para>
///       Like its pair, every test runs the same query string twice — once with the mandated settings and once with
///       <see cref="ODataConfiguration.MisconfiguredQuerySettings" />, which leaves the setting untouched rather than
///       forcing it to <c>True</c>. What they observe is therefore what OData's namespace matching against its hardcoded
///       provider allowlist actually decides for this library's provider, so the day the provider joins that list these
///       tests say so instead of quietly continuing to pass.
///    </para>
/// </remarks>
public class RelationMisconfigurationRegressionTests : RelationConformanceTestBase
{
   public RelationMisconfigurationRegressionTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public void Expand_ToManyRows_ComesBackEmptyWhenNullPropagationIsLeftAtItsDefault()
   {
      const string queryString = "$expand=Books&$orderby=Name";

      var misconfigured = MisconfiguredAuthors(queryString).ProjectedRows();

      // Every author, in the right order, with the right scalar values — and not one book. Nothing in the response says
      // the endpoint is misconfigured: an empty collection is exactly what an author with no books looks like.
      misconfigured.Select(x => x["Name"]).Should().Equal("gaiman", "lewis", "pratchett", "tolkien");
      misconfigured.Should().AllSatisfy(row => ExpandedRows(row, nameof(AuthorData.Books)).Should().BeEmpty());

      var configured = ApplyToAuthors(queryString).ProjectedRows();

      ExpandedRows(configured[3], nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("hobbit", "silmarillion");
   }

   [Fact]
   public void Expand_ToManyRows_SendsTheSameStatementEitherWay()
   {
      const string queryString = "$expand=Books&$orderby=Name";

      // The reason it is undetectable from the outside as well as from the inside: the query surface composed exactly the
      // same statement, so there is no failure to catch and no diagnostic to read. The rewriting happens above it, in the
      // projection OData binds the expansion as.
      MisconfiguredAuthors(queryString).RenderSql().Should().Be(ApplyToAuthors(queryString).RenderSql());
   }

   [Fact]
   public void Expand_ToOneRow_IsUnaffectedByTheNullPropagationDefault()
   {
      // Worth knowing before auditing an endpoint: only the to-many direction loses its rows. A to-one expansion folds
      // into the main statement as a join and survives the rewriting intact.
      const string queryString = "$expand=Author&$orderby=Title";

      var misconfigured = MisconfiguredBooks(queryString).ProjectedRows();

      ExpandedRow(misconfigured[0], nameof(BookData.Author))!["Name"].Should().Be("tolkien");
      ExpandedRow(misconfigured[2], nameof(BookData.Author)).Should().BeNull();

      misconfigured.Should().BeEquivalentTo(ApplyToBooks(queryString).ProjectedRows());
   }

   [Fact]
   public async Task Filter_WithAnAllQuantifier_ReturnsTheWrongRowsWhenNullPropagationIsLeftAtItsDefault()
   {
      const string queryString = "$filter=Books/all(book: book/Title eq 'hobbit')&$orderby=Name";

      var misconfigured = MisconfiguredAuthors(queryString);

      // The guard is what breaks it: OData adds an EXISTS on top of its own NOT EXISTS, so a parent with an empty
      // collection no longer qualifies — and an empty collection satisfying "all" vacuously is the specified behaviour.
      misconfigured.RenderSql().Should().ContainAll("NOT EXISTS(", "EXISTS(");

      (await NamesAsync(misconfigured)).Should().BeEmpty();

      // Not a translation failure and not an error: a different, wrong row set.
      (await NamesAsync(ApplyToAuthors(queryString))).Should().Equal("gaiman", "pratchett");
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
