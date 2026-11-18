using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

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

      sql.Should().NotContain("'tolkien'");

      (await TitlesAsync(applied)).Should().Equal("hobbit", "silmarillion");
   }

   [Fact]
   public async Task OrderBy_ThroughARelationProperty_SortsInTheDatabase()
   {
      var applied = ApplyToBooks("$orderby=Author/Name desc,Title");

      applied.RenderSql().Should().ContainAll("LEFT JOIN", "ORDER BY", "DESC");

      (await TitlesAsync(applied)).Should().Equal("orphan", "hobbit", "silmarillion", "narnia");
   }

   [Fact]
   public async Task Filter_WithAnAnyQuantifier_ReachesTheDatabaseAsAnExistsSubquery()
   {
      var applied = ApplyToAuthors("$filter=Books/any(book: book/Title eq 'narnia')");

      applied.RenderSql().Should().ContainAll("EXISTS(", "odata_books");

      (await NamesAsync(applied)).Should().Equal("lewis");
   }

   [Fact]
   public async Task Filter_WithAnAllQuantifier_ReachesTheDatabaseAsANegatedExistsSubquery()
   {
      var applied = ApplyToAuthors("$filter=Books/all(book: book/Title eq 'hobbit')&$orderby=Name");

      applied.RenderSql().Should().ContainAll("NOT EXISTS(", "odata_books");

      (await NamesAsync(applied)).Should().Equal("gaiman", "pratchett");
   }
}
