using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Schema;

namespace mvdmio.Database.PgSQL.Tests.Unit.Connectors.Schema;

public class SchemaExtractorTests
{
   [Fact]
   public void EscapeSqlString_WithNull_ReturnsEmptyString()
   {
      var result = SchemaExtractor.EscapeSqlString(null);

      result.Should().BeEmpty();
   }

   [Fact]
   public void EscapeSqlString_WithEmptyString_ReturnsEmptyString()
   {
      var result = SchemaExtractor.EscapeSqlString(string.Empty);

      result.Should().BeEmpty();
   }

   [Fact]
   public void EscapeSqlString_WithNoSpecialCharacters_ReturnsUnchanged()
   {
      var result = SchemaExtractor.EscapeSqlString("fk_child_parent");

      result.Should().Be("fk_child_parent");
   }

   [Fact]
   public void EscapeSqlString_WithSingleQuote_EscapesQuote()
   {
      var result = SchemaExtractor.EscapeSqlString("it's a test");

      result.Should().Be("it''s a test");
   }

   [Fact]
   public void EscapeSqlString_WithMultipleSingleQuotes_EscapesAllQuotes()
   {
      var result = SchemaExtractor.EscapeSqlString("it's a 'test'");

      result.Should().Be("it''s a ''test''");
   }

   [Fact]
   public void EscapeSqlString_WithOnlySingleQuote_EscapesQuote()
   {
      var result = SchemaExtractor.EscapeSqlString("'");

      result.Should().Be("''");
   }
}
