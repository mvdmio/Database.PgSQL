using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

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

      syncFailure.Should().BeOfType<QueryTranslationException>();
      asyncFailure.Should().BeOfType<QueryTranslationException>();

      asyncFailure!.Message.Should().Contain("Regex");
   }

   [Fact]
   public async Task Apply_WithAFilterThatFailsInTheDatabase_ThrowsAQueryExceptionCarryingTheSql()
   {
      var applied = Apply("$filter=Rating div 0 eq 1");

      var failure = await Record.ExceptionAsync(() => applied.RowsAsync<SampleData>(CancellationToken));

      failure.Should().BeOfType<QueryException>();
      ((QueryException)failure!).Sql.Should().Contain("odata_samples");
   }
}
