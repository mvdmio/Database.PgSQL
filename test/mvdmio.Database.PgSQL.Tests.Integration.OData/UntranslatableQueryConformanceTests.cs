using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    What a query that cannot be answered does. The query surface never falls back to filtering in memory, so an
///    untranslatable expression is a refusal rather than a slow success — and the exception says which side gave up.
/// </summary>
public class UntranslatableQueryConformanceTests : SampleConformanceTestBase
{
   public UntranslatableQueryConformanceTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public async Task Apply_WithAnUntranslatableFilter_ThrowsTheLibrarysTranslationException()
   {
      var applied = Apply("$filter=matchespattern(Name, '^a')");

      var syncFailure = Record.Exception(() => ((IQueryable<SampleData>)applied.Query).ToList());
      var asyncFailure = await Record.ExceptionAsync(() => applied.RowsAsync<SampleData>(CancellationToken));

      // Not the provider's own exception type, and not an empty or in-memory-filtered result.
      syncFailure.Should().BeOfType<QueryTranslationException>();
      asyncFailure.Should().BeOfType<QueryTranslationException>();

      // Enough to tell which side is at fault: the message names the construct OData composed, so the failure reads as
      // "the front end asked for a regular expression" rather than "something went wrong".
      asyncFailure!.Message.Should().Contain("Regex");
   }

   [Fact]
   public async Task Apply_WithAFilterThatFailsInTheDatabase_ThrowsAQueryExceptionCarryingTheSql()
   {
      // The other half of "whose fault is it": this one did translate, reached PostgreSQL and failed there, so it is a
      // QueryException with the statement attached rather than a translation failure.
      var applied = Apply("$filter=Rating div 0 eq 1");

      var failure = await Record.ExceptionAsync(() => applied.RowsAsync<SampleData>(CancellationToken));

      failure.Should().BeOfType<QueryException>();
      ((QueryException)failure!).Sql.Should().Contain("odata_samples");
   }
}
