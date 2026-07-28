using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    What a query string reaching through a relation property translates to: a navigation path in <c>$filter</c> and
///    <c>$orderby</c>, and the two collection quantifiers. None of these fetch the related rows — they only constrain or
///    sort by them — so the element type is unchanged and the rows come back typed.
/// </summary>
public class RelationNavigationConformanceTests : RelationConformanceTestBase
{
   public RelationNavigationConformanceTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public async Task Filter_ThroughARelationProperty_ReachesTheDatabaseAsAJoinedPredicate()
   {
      var applied = ApplyToBooks("$filter=Author/Name eq 'tolkien'&$orderby=Title");
      var sql = applied.RenderSql();

      sql.Should().ContainAll("LEFT JOIN", "odata_authors", "WHERE");

      // Parameterized rather than inlined, exactly as a predicate on the queried table is.
      sql.Should().NotContain("'tolkien'");

      (await TitlesAsync(applied)).Should().Equal("hobbit", "silmarillion");
   }

   [Fact]
   public async Task OrderBy_ThroughARelationProperty_SortsInTheDatabase()
   {
      var applied = ApplyToBooks("$orderby=Author/Name desc,Title");

      applied.RenderSql().Should().ContainAll("LEFT JOIN", "ORDER BY", "DESC");

      // The relation is an outer join, so the book whose foreign key points nowhere is still returned and sorts with the
      // nulls — PostgreSQL orders them first under DESC.
      (await TitlesAsync(applied)).Should().Equal("orphan", "hobbit", "silmarillion", "narnia");
   }

   [Fact]
   public async Task Filter_WithAnAnyQuantifier_ReachesTheDatabaseAsAnExistsSubquery()
   {
      var applied = ApplyToAuthors("$filter=Books/any(book: book/Title eq 'narnia')");

      // A correlated EXISTS rather than a join, so a parent matching twice is still returned once.
      applied.RenderSql().Should().ContainAll("EXISTS(", "odata_books");

      (await NamesAsync(applied)).Should().Equal("lewis");
   }

   [Fact]
   public async Task Filter_WithAnAllQuantifier_ReachesTheDatabaseAsANegatedExistsSubquery()
   {
      var applied = ApplyToAuthors("$filter=Books/all(book: book/Title eq 'hobbit')&$orderby=Name");

      applied.RenderSql().Should().ContainAll("NOT EXISTS(", "odata_books");

      // The two authors with no books at all, and neither of the two with books: an empty collection satisfies "all"
      // vacuously, which is what OData Part 2 specifies. An endpoint replacing one backed by a provider that drops those
      // rows will see the difference.
      (await NamesAsync(applied)).Should().Equal("gaiman", "pratchett");
   }

   [Fact]
   public async Task Filter_WithAnAnyQuantifierAndNoPredicate_AsksOnlyWhetherTheCollectionHasRows()
   {
      var applied = ApplyToAuthors("$filter=Books/any()&$orderby=Name");

      applied.RenderSql().Should().Contain("EXISTS(");

      (await NamesAsync(applied)).Should().Equal("lewis", "tolkien");
   }
}
