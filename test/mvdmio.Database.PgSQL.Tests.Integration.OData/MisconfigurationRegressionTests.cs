using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    Pins what happens when the mandatory null-propagation setting is left at its default, so nobody simplifies
///    <see cref="ODataConfiguration.QuerySettings" /> away without a failing test.
/// </summary>
/// <remarks>
///    <para>
///       OData chooses the default by matching the query provider's namespace against a hardcoded allowlist of
///       Microsoft providers. This library's provider is not on it and cannot join it from our side, so OData assumes an
///       in-memory sequence and guards every property access with a null check. These tests pass
///       <see cref="ODataConfiguration.MisconfiguredQuerySettings" />, which leaves the setting untouched rather than
///       forcing it — so what they observe is what that namespace matching actually decides.
///    </para>
///    <para>
///       This is a deliberately narrow guard, and weaker than the risk warrants. The setting's worst symptom is
///       <c>$expand</c> silently returning empty collections, which is out of scope here because the library has no
///       relation model — so what remains provable is the two symptoms below. The upstream request to make the correct
///       behaviour automatic has been open since 2022 with no answer; it is tracked in
///       <c>.agents/ideas/odata-provider-allowlist-upstream.md</c>, which is where to look before assuming this can be deleted.
///    </para>
/// </remarks>
public class MisconfigurationRegressionTests : SampleConformanceTestBase
{
   public MisconfigurationRegressionTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public void Filter_WithSubstring_FailsToTranslateWhenNullPropagationIsLeftAtItsDefault()
   {
      const string queryString = "$filter=substring(Name, 1, 3) eq 'lic'";

      var failure = Record.Exception(() => Misconfigured(queryString).RenderSql());

      failure.Should().BeOfType<QueryTranslationException>();

      // The guard is what breaks it: OData rewrites the call as a conditional over a helper of its own that has no SQL
      // translation. With the setting disabled the same filter is a plain Substring.
      failure!.Message.Should().Contain("ClrSafeFunctions");

      Apply(queryString).RenderSql().Should().Contain("Substring(");
   }

   [Fact]
   public void Filter_WithAStringFunction_RendersANonSargableNullGuardWhenNullPropagationIsLeftAtItsDefault()
   {
      const string queryString = "$filter=length(Name) eq 5";

      // A CASE over "column IS NULL" wrapped around the predicate: correct rows, but PostgreSQL cannot use an index
      // for it. On a column the model already declares non-nullable, the guard can never even fire.
      Misconfigured(queryString).RenderSql().Should().ContainAll("CASE", "IS NULL");

      var configured = Apply(queryString).RenderSql();

      configured.Should().NotContain("CASE");
      configured.Should().Contain("Length(");
   }

   [Fact]
   public async Task Filter_WithAStringFunction_ReturnsTheSameRowsEitherWay()
   {
      // The reason the misconfiguration is dangerous rather than merely slow: the rows are right, so nothing about the
      // response tells a consumer that anything is wrong.
      const string queryString = "$filter=length(Name) eq 5";

      var misconfigured = await NamesAsync(Misconfigured(queryString));
      var configured = await NamesAsync(Apply(queryString));

      misconfigured.Should().BeEquivalentTo(configured);
      configured.Should().BeEquivalentTo("alice", "carol");
   }

   private AppliedQuery Misconfigured(string queryString)
   {
      return ODataQuery.Apply(Repository.Query(), queryString, ODataConfiguration.MisconfiguredQuerySettings);
   }
}
