using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

public class MisconfigurationRegressionTests : SampleConformanceTestBase
{
   public MisconfigurationRegressionTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public void Filter_WithSubstring_FailsToTranslateWhenNullPropagationIsLeftAtItsDefault()
   {
      const string QUERY_STRING = "$filter=substring(Name, 1, 3) eq 'lic'";

      var failure = Record.Exception(() => Misconfigured(QUERY_STRING).RenderSql());

      failure.Should().BeOfType<QueryTranslationException>();

      failure!.Message.Should().Contain("ClrSafeFunctions");

      Apply(QUERY_STRING).RenderSql().Should().Contain("Substring(");
   }

   [Fact]
   public void Filter_WithAStringFunction_RendersANonSargableNullGuardWhenNullPropagationIsLeftAtItsDefault()
   {
      const string QUERY_STRING = "$filter=length(Nickname) eq 3";

      Misconfigured(QUERY_STRING).RenderSql().Should().ContainAll("CASE", "IS NULL");

      var configured = Apply(QUERY_STRING).RenderSql();

      configured.Should().NotContain("CASE");
      configured.Should().Contain("Length(");
   }

   [Fact]
   public void Filter_WithAStringFunctionOverANonNullableColumn_RendersNoGuardEitherWay()
   {
      Misconfigured("$filter=length(Name) eq 5").RenderSql().Should().NotContain("CASE");
   }

   [Fact]
   public async Task Filter_WithAStringFunction_ReturnsTheSameRowsEitherWay()
   {
      const string QUERY_STRING = "$filter=length(Nickname) eq 3";

      var misconfigured = await NamesAsync(Misconfigured(QUERY_STRING));
      var configured = await NamesAsync(Apply(QUERY_STRING));

      misconfigured.Should().BeEquivalentTo(configured);
      configured.Should().BeEquivalentTo("carol");
   }

   private AppliedQuery Misconfigured(string queryString)
   {
      return ODataQuery.Apply(Repository.Query(), queryString, ODataConfiguration.MisconfiguredQuerySettings);
   }
}
