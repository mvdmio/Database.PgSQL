using AwesomeAssertions;
using Microsoft.OData;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    One test per <c>$expand</c> construct, asserting the rows returned and — where the expansion reaches the database
///    in the query's own statement — the shape of the SQL.
/// </summary>
/// <remarks>
///    <para>
///       OData does not implement <c>$expand</c> by calling an eager-loading operator. It binds the expansion as a
///       projection into wrapper types of its own and selects the relation property inside that projection, so this
///       library's <c>Include</c> and <c>ThenInclude</c> operators are not on this path at all. What makes the projected
///       member translatable is the provider-level association registration emitted for each relation, which is what
///       these tests exercise indirectly.
///    </para>
///    <para>
///       Statement counts are deliberately not asserted: the suite can see the SQL a composed query renders to and the
///       last statement sent through the connection, and nothing collects the rest. Where that limit bites it is called
///       out at the assertion.
///    </para>
/// </remarks>
public class ExpandConformanceTests : RelationConformanceTestBase
{
   public ExpandConformanceTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public void Expand_ToOneRow_FoldsIntoTheQueryAsAnOuterJoin()
   {
      var applied = ApplyToBooks("$expand=Author&$orderby=Title");
      var sql = applied.RenderSql();

      // One statement, and the related columns are in it: a to-one expansion costs no extra round trip.
      sql.Should().ContainAll("LEFT JOIN", "odata_authors");
      sql.Should().Contain(".name");

      var rows = applied.ProjectedRows();

      rows.Select(x => x["Title"]).Should().Equal("hobbit", "narnia", "orphan", "silmarillion");
      rows[0].Expanded(nameof(BookData.Author))!["Name"].Should().Be("tolkien");
      rows[1].Expanded(nameof(BookData.Author))!["Name"].Should().Be("lewis");
   }

   [Fact]
   public void Expand_ToOneRowAcrossANullForeignKey_YieldsAnAbsentRelationPropertyRatherThanAnError()
   {
      // A relation is always an outer join, so the row itself is still returned and the expanded value is simply null.
      // An endpoint over ordinary data with unset foreign keys does not fail.
      var rows = ApplyToBooks("$filter=Title eq 'orphan'&$expand=Author").ProjectedRows();

      rows.Should().ContainSingle();
      rows[0].Expanded(nameof(BookData.Author)).Should().BeNull();
   }

   [Fact]
   public void Expand_ToManyRows_ReturnsTheRelatedRowsWithoutPuttingThemInTheQuerysOwnStatement()
   {
      var applied = ApplyToAuthors("$expand=Books&$orderby=Name");

      // Unlike the to-one case above, the related table is nowhere in the statement the query renders to — so at least
      // one further statement must run to fetch the books below.
      applied.RenderSql().Should().NotContain("odata_books");

      var rows = applied.ProjectedRows();

      rows.Select(x => x["Name"]).Should().Equal("gaiman", "lewis", "pratchett", "tolkien");
      rows[3].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("hobbit", "silmarillion");
      rows[1].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("narnia");
   }

   [Fact]
   public void Expand_ToManyRows_LeavesTheDetailStatementUnobservable()
   {
      // Why no test in this suite asserts a statement count, and why the walkthrough does not state one. The provider
      // runs a detail query ahead of the query that derives its parents, so after materializing an expansion the last
      // statement sent is the main query — the detail statement has already been and gone, and nothing collects it.
      // Making it visible would take a diagnostics facility, which is deliberately not being added.
      var applied = ApplyToAuthors("$expand=Books&$orderby=Name");

      applied.ProjectedRows()[3].ExpandedMany(nameof(AuthorData.Books)).Should().HaveCount(2);

      applied.LastSql().Should().Be(applied.RenderSql());
   }

   [Fact]
   public void Expand_ToManyRowsWhereThereAreNone_YieldsAnEmptyCollection()
   {
      // "None" and "not asked for" are the same value here — an empty collection — so a client tells them apart by
      // whether it sent $expand, not by what came back.
      var rows = ApplyToAuthors("$filter=Name eq 'pratchett'&$expand=Books").ProjectedRows();

      rows.Should().ContainSingle();
      rows[0].ExpandedMany(nameof(AuthorData.Books)).Should().BeEmpty();
   }

   [Fact]
   public void Expand_WithANestedFilter_NarrowsTheRelatedRows()
   {
      var rows = ApplyToAuthors("$expand=Books($filter=Title eq 'hobbit')&$orderby=Name").ProjectedRows();

      // The nested filter narrows the detail rows themselves rather than deciding which parents get them: every author
      // is still returned, and only tolkien's matching book comes back.
      rows.Select(x => x["Name"]).Should().Equal("gaiman", "lewis", "pratchett", "tolkien");
      rows[3].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("hobbit");
      rows[1].ExpandedMany(nameof(AuthorData.Books)).Should().BeEmpty();
   }

   [Fact]
   public void Expand_WithANestedSelect_NarrowsTheRelatedColumns()
   {
      var rows = ApplyToAuthors("$expand=Books($select=Title)&$orderby=Name").ProjectedRows();

      var books = rows[3].ExpandedMany(nameof(AuthorData.Books));

      books.Select(x => x["Title"]).Should().Equal("hobbit", "silmarillion");

      // Unlike a top-level $select, which keeps the key so the entity stays addressable, a nested one narrows to exactly
      // what was asked for.
      books[0].Keys.Should().Equal("Title");
   }

   [Fact]
   public void Expand_WithANestedOrderByAndTop_SortsAndLimitsTheRelatedRows()
   {
      var rows = ApplyToAuthors("$expand=Books($orderby=Title desc;$top=1)&$orderby=Name").ProjectedRows();

      // Applied per parent rather than across the whole detail set: tolkien keeps his last book by title, and lewis
      // still gets his one.
      rows[3].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("silmarillion");
      rows[1].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("narnia");
   }

   [Fact]
   public void Expand_WithANestedCount_ReachesTheDatabaseAsACorrelatedSubqueryInTheQuerysOwnStatement()
   {
      var applied = ApplyToAuthors("$expand=Books($count=true)&$orderby=Name");
      var sql = applied.RenderSql();

      // The count costs no round trip of its own: it is a correlated aggregate over the related table, in the same
      // statement as the parents, even though the rows themselves are not.
      sql.Should().ContainAll("COUNT(", "odata_books");

      var rows = applied.ProjectedRows();

      rows[3].ExpandedMany(nameof(AuthorData.Books)).Should().HaveCount(2);
      rows[0].ExpandedMany(nameof(AuthorData.Books)).Should().BeEmpty();
   }

   [Fact]
   public void Expand_NestedTwoLevelsDeep_ReturnsTheRelatedRowsOfTheRelatedRows()
   {
      var rows = ApplyToAuthors("$expand=Books($expand=Author)&$orderby=Name").ProjectedRows();

      var tolkiensBooks = rows[3].ExpandedMany(nameof(AuthorData.Books));

      tolkiensBooks.Select(x => x["Title"]).Should().Equal("hobbit", "silmarillion");
      tolkiensBooks[0].Expanded(nameof(BookData.Author))!["Name"].Should().Be("tolkien");
   }

   [Fact]
   public void Expand_WithLevelsOverASelfReference_WalksExactlyThatManyHops()
   {
      var applied = ApplyToAuthors("$expand=Mentor($levels=2)&$orderby=Name");
      var joins = applied.RenderSql().Split("LEFT JOIN").Length - 1;

      joins.Should().Be(2, "two hops of a to-one relation fold into one statement as two joins against the same table");

      var rows = applied.ProjectedRows();
      var gaimansMentor = rows[0].Expanded(nameof(AuthorData.Mentor))!;

      gaimansMentor["Name"].Should().Be("pratchett");
      gaimansMentor.Expanded(nameof(AuthorData.Mentor))!["Name"].Should().Be("lewis");

      // The chain continues to tolkien in the data, and $levels=2 stops before it — which is what tells a bounded walk
      // apart from an unbounded one.
      gaimansMentor.Expanded(nameof(AuthorData.Mentor))!.Keys.Should().NotContain(nameof(AuthorData.Mentor));
   }

   [Fact]
   public void Expand_Everything_ExpandsEveryRelationPropertyOneLevelDeep()
   {
      var rows = ApplyToAuthors("$expand=*&$orderby=Name").ProjectedRows();

      // All three relations the table definition declares, and nothing beyond one level: a client can ask for the whole
      // first ring of the model in one request.
      rows[3].Keys.Should().BeEquivalentTo(
         nameof(AuthorData.AuthorId),
         nameof(AuthorData.Name),
         nameof(AuthorData.MentorId),
         nameof(AuthorData.Mentor),
         nameof(AuthorData.Mentees),
         nameof(AuthorData.Books)
      );

      rows[3].Expanded(nameof(AuthorData.Mentor)).Should().BeNull();
      rows[3].ExpandedMany(nameof(AuthorData.Mentees)).Select(x => x["Name"]).Should().Equal("lewis");
      rows[3].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("hobbit", "silmarillion");
   }

   [Fact]
   public void Expand_DeeperThanTheConfiguredCap_IsAValidationErrorRatherThanATranslationFailure()
   {
      // The same error contract the suite draws for a blocked $filter function: a client error before anything reaches
      // the database, not a server error from the provider afterwards.
      var failure = Record.Exception(() => ODataQuery.Validate<AuthorData>("$expand=Books($expand=Author($expand=Books))", ODataConfiguration.RelationModel));

      failure.Should().BeOfType<ODataException>();
      failure!.Message.Should().Contain($"The maximum depth allowed is {ODataConfiguration.MAX_EXPANSION_DEPTH}");

      // The deepest construct this suite covers is admitted, so the cap bounds a cycle without blocking real requests.
      ODataQuery.Validate<AuthorData>("$expand=Books($expand=Author)", ODataConfiguration.RelationModel);
   }

   [Fact]
   public void Expand_WithLevelsDeeperThanTheConfiguredCap_IsTheSameValidationError()
   {
      // $levels counts against the same cap, which matters because a self-reference is the cheapest way to walk a cycle.
      var failure = Record.Exception(() => ODataQuery.Validate<AuthorData>("$expand=Mentor($levels=3)", ODataConfiguration.RelationModel));

      failure.Should().BeOfType<ODataException>();
   }
}
