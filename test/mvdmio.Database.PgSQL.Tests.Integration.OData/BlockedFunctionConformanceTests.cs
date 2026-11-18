using AwesomeAssertions;
using Microsoft.OData;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

public class BlockedFunctionConformanceTests : SampleConformanceTestBase
{
   public BlockedFunctionConformanceTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Theory]
   [InlineData("matchespattern", "matchespattern(Name, '^a')")]
   [InlineData("isof", "isof(Name, Edm.String)")]
   public void Validate_WithAnExcludedFunction_ReportsAClientError(string function, string filter)
   {
      var failure = Record.Exception(() => ODataQuery.Validate<SampleData>($"$filter={filter}"));

      failure.Should().BeOfType<ODataException>($"'{function}' is excluded from the allowed-function set");
      failure!.Message.Should().Contain("is not allowed");
   }

   [Theory]
   [InlineData("matchespattern", "matchespattern(Name, '^a')")]
   [InlineData("isof", "isof(Name, Edm.String)")]
   public void Apply_WithAnExcludedFunction_WouldOtherwiseFailInTheQueryProvider(string function, string filter)
   {
      var failure = Record.Exception(() => Apply($"$filter={filter}").RenderSql());

      failure.Should().BeOfType<QueryTranslationException>($"'{function}' has no SQL translation");
   }

   [Theory]
   [InlineData("mindatetime", "CreatedAt gt mindatetime()")]
   [InlineData("maxdatetime", "CreatedAt lt maxdatetime()")]
   [InlineData("totaloffsetminutes", "totaloffsetminutes(CreatedAt) eq 0")]
   public void Apply_WithAFunctionODataDoesNotImplement_PassesValidationAndThenFailsInsideOData(string function, string filter)
   {
      ODataQuery.Validate<SampleData>($"$filter={filter}");

      var failure = Record.Exception(() => Apply($"$filter={filter}"));

      failure.Should().BeOfType<NotImplementedException>();
      failure!.Message.Should().Contain(function);
   }
}
