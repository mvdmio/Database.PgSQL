using AwesomeAssertions;
using Microsoft.OData;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

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
      var rows = ApplyToBooks("$filter=Title eq 'orphan'&$expand=Author").ProjectedRows();

      rows.Should().ContainSingle();
      rows[0].Expanded(nameof(BookData.Author)).Should().BeNull();
   }

   [Fact]
   public void Expand_ToManyRows_ReturnsTheRelatedRowsWithoutPuttingThemInTheQuerysOwnStatement()
   {
      var applied = ApplyToAuthors("$expand=Books&$orderby=Name");

      applied.RenderSql().Should().NotContain("odata_books");

      var rows = applied.ProjectedRows();

      rows.Select(x => x["Name"]).Should().Equal("gaiman", "lewis", "pratchett", "tolkien");
      rows[3].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("hobbit", "silmarillion");
      rows[1].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("narnia");
   }

   [Fact]
   public void Expand_ToManyRows_LeavesTheDetailStatementUnobservable()
   {
      var applied = ApplyToAuthors("$expand=Books&$orderby=Name");

      applied.ProjectedRows()[3].ExpandedMany(nameof(AuthorData.Books)).Should().HaveCount(2);

      applied.LastSql().Should().Be(applied.RenderSql());
   }

   [Fact]
   public void Expand_ToManyRowsWhereThereAreNone_YieldsAnEmptyCollection()
   {
      var rows = ApplyToAuthors("$filter=Name eq 'pratchett'&$expand=Books").ProjectedRows();

      rows.Should().ContainSingle();
      rows[0].ExpandedMany(nameof(AuthorData.Books)).Should().BeEmpty();
   }

   [Fact]
   public void Expand_WithANestedFilter_NarrowsTheRelatedRows()
   {
      var rows = ApplyToAuthors("$expand=Books($filter=Title eq 'hobbit')&$orderby=Name").ProjectedRows();

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

      books[0].Keys.Should().Equal("Title");
   }

   [Fact]
   public void Expand_WithANestedOrderByAndTop_SortsAndLimitsTheRelatedRows()
   {
      var rows = ApplyToAuthors("$expand=Books($orderby=Title desc;$top=1)&$orderby=Name").ProjectedRows();

      rows[3].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("silmarillion");
      rows[1].ExpandedMany(nameof(AuthorData.Books)).Select(x => x["Title"]).Should().Equal("narnia");
   }

   [Fact]
   public void Expand_WithANestedCount_ReachesTheDatabaseAsACorrelatedSubqueryInTheQuerysOwnStatement()
   {
      var applied = ApplyToAuthors("$expand=Books($count=true)&$orderby=Name");
      var sql = applied.RenderSql();

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

      gaimansMentor.Expanded(nameof(AuthorData.Mentor))!.Keys.Should().NotContain(nameof(AuthorData.Mentor));
   }

   [Fact]
   public void Expand_Everything_ExpandsEveryRelationPropertyOneLevelDeep()
   {
      var rows = ApplyToAuthors("$expand=*&$orderby=Name").ProjectedRows();

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
      var failure = Record.Exception(() => ODataQuery.Validate<AuthorData>("$expand=Books($expand=Author($expand=Books))", ODataConfiguration.RelationModel));

      failure.Should().BeOfType<ODataException>();
      failure!.Message.Should().Contain($"The maximum depth allowed is {ODataConfiguration.MAX_EXPANSION_DEPTH}");

      ODataQuery.Validate<AuthorData>("$expand=Books($expand=Author)", ODataConfiguration.RelationModel);
   }

   [Fact]
   public void Expand_WithLevelsDeeperThanTheConfiguredCap_IsTheSameValidationError()
   {
      var failure = Record.Exception(() => ODataQuery.Validate<AuthorData>("$expand=Mentor($levels=3)", ODataConfiguration.RelationModel));

      failure.Should().BeOfType<ODataException>();
   }
}
