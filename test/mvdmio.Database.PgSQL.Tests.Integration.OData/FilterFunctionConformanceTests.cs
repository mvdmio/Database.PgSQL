using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    One case per <c>$filter</c> function, grouped by family so a family's results read as a table. Each case names
///    the function, the filter it stands for, and the rows it must return; a function that did not translate would
///    surface as <c>QueryTranslationException</c> rather than a wrong row set.
/// </summary>
public class FilterFunctionConformanceTests : SampleConformanceTestBase
{
   public FilterFunctionConformanceTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Theory]
   [InlineData("contains", "contains(Name, 'a')", new[] { "alice", "carol", "dave" })]
   [InlineData("startswith", "startswith(Name, 'b')", new[] { "bob" })]
   [InlineData("endswith", "endswith(Name, 'e')", new[] { "alice", "dave" })]
   [InlineData("indexof", "indexof(Name, 'o') eq 1", new[] { "bob" })]
   [InlineData("length", "length(Name) eq 5", new[] { "alice", "carol" })]
   [InlineData("substring/2", "substring(Name, 1) eq 'lice'", new[] { "alice" })]
   [InlineData("substring/3", "substring(Name, 1, 3) eq 'lic'", new[] { "alice" })]
   [InlineData("tolower", "tolower(Name) eq 'bob'", new[] { "bob" })]
   [InlineData("toupper", "toupper(Name) eq 'BOB'", new[] { "bob" })]
   [InlineData("trim", "trim(Name) eq 'bob'", new[] { "bob" })]
   [InlineData("concat", "concat(Name, Tier) eq 'alicegold'", new[] { "alice" })]
   public async Task Filter_WithAStringFunction_ReturnsTheMatchingRows(string function, string filter, string[] expected)
   {
      await AssertFilterAsync(function, filter, expected);
   }

   [Theory]
   [InlineData("year", "year(CreatedAt) eq 2024", new[] { "alice" })]
   [InlineData("month", "month(CreatedAt) eq 11", new[] { "bob" })]
   [InlineData("day", "day(CreatedAt) eq 15", new[] { "dave" })]
   [InlineData("hour", "hour(CreatedAt) eq 22", new[] { "bob" })]
   [InlineData("minute", "minute(CreatedAt) eq 15", new[] { "bob" })]
   [InlineData("second", "second(CreatedAt) eq 45", new[] { "bob" })]
   [InlineData("date", "date(CreatedAt) eq 2022-01-01", new[] { "carol" })]
   [InlineData("time", "time(CreatedAt) eq 12:30:00", new[] { "dave" })]
   [InlineData("fractionalseconds", "fractionalseconds(CreatedAt) eq 0", new[] { "alice", "bob", "carol", "dave" })]
   [InlineData("now", "now() gt CreatedAt", new[] { "alice", "bob", "carol", "dave" })]
   public async Task Filter_WithADatePartFunction_ReturnsTheMatchingRows(string function, string filter, string[] expected)
   {
      await AssertFilterAsync(function, filter, expected);
   }

   [Theory]
   [InlineData("round", "round(Amount) eq 6", new[] { "dave" })]
   [InlineData("floor", "floor(Amount) eq 5", new[] { "dave" })]
   [InlineData("ceiling", "ceiling(Amount) eq 6", new[] { "dave" })]
   [InlineData("add", "Rating add 2 eq 5", new[] { "alice" })]
   [InlineData("sub", "Rating sub 1 eq 2", new[] { "alice" })]
   [InlineData("mul", "Rating mul 2 eq 16", new[] { "carol" })]
   [InlineData("div", "Rating div 2 eq 4", new[] { "carol" })]
   [InlineData("mod", "Rating mod 2 eq 0", new[] { "carol" })]
   public async Task Filter_WithAnArithmeticFunction_ReturnsTheMatchingRows(string function, string filter, string[] expected)
   {
      await AssertFilterAsync(function, filter, expected);
   }

   [Theory]
   [InlineData("cast to string", "cast(Rating, Edm.String) eq '8'", new[] { "carol" })]
   [InlineData("cast to decimal", "cast(Rating, Edm.Decimal) eq 8", new[] { "carol" })]
   public async Task Filter_WithACast_ReturnsTheMatchingRows(string function, string filter, string[] expected)
   {
      await AssertFilterAsync(function, filter, expected);
   }

   [Theory]
   [InlineData("in", "Name in ('bob','carol')", new[] { "bob", "carol" })]
   [InlineData("enum equality", $"Category eq {ODataConfiguration.EDM_NAMESPACE}.{nameof(SampleCategory)}'Legacy'", new[] { "carol" })]
   [InlineData("boolean property", "IsActive", new[] { "alice", "carol" })]
   [InlineData("guid equality", "PublicId eq aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", new[] { "alice" })]
   public async Task Filter_WithAMembershipOrValueComparison_ReturnsTheMatchingRows(string function, string filter, string[] expected)
   {
      await AssertFilterAsync(function, filter, expected);
   }

   private async Task AssertFilterAsync(string function, string filter, string[] expected)
   {
      var applied = Apply($"$filter={filter}");
      var names = await NamesAsync(applied);

      names.Should().BeEquivalentTo(expected, $"'{function}' must reach the database. SQL: {applied.RenderSql()}");
   }
}
