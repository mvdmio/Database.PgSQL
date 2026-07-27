using AwesomeAssertions;
using Microsoft.OData;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    The functions that do not reach SQL, and what a client gets instead. The difference matters when a conformance
///    test fails: a validation error is the configuration working as intended, a translation exception is the query
///    provider refusing, and a <see cref="NotImplementedException" /> is OData itself.
/// </summary>
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
      // What validation is protecting the caller from: without it the function reaches the provider, which refuses to
      // translate it. Same outcome for the caller in-process, but at a hosted endpoint the two are a client error and a
      // server error respectively.
      var failure = Record.Exception(() => Apply($"$filter={filter}").RenderSql());

      failure.Should().BeOfType<QueryTranslationException>($"'{function}' has no SQL translation");
   }

   [Theory]
   [InlineData("mindatetime", "CreatedAt gt mindatetime()")]
   [InlineData("maxdatetime", "CreatedAt lt maxdatetime()")]
   [InlineData("totaloffsetminutes", "totaloffsetminutes(CreatedAt) eq 0")]
   public void Apply_WithAFunctionODataDoesNotImplement_PassesValidationAndThenFailsInsideOData(string function, string filter)
   {
      // These three cannot be excluded by configuration: this version of OData has no AllowedFunctions member for any of
      // them, so there is no bit to clear and validation has nothing to object to.
      ODataQuery.Validate<SampleData>($"$filter={filter}");

      // OData's own expression binder then gives up. The fault is the front end's, not the query surface's — nothing
      // reaches the provider at all, which is why this is a NotImplementedException rather than a translation failure.
      var failure = Record.Exception(() => Apply($"$filter={filter}"));

      failure.Should().BeOfType<NotImplementedException>();
      failure!.Message.Should().Contain(function);
   }
}
