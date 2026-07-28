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
///       These are the symptoms visible on a single table. The two that need a relation to see — an expanded collection
///       coming back empty, and a collection <c>all()</c> returning the wrong rows — are in
///       <see cref="RelationMisconfigurationRegressionTests" />, which is bound to the relation fixture rather than this
///       one. The upstream request to make the correct behaviour automatic has been open since 2022 with no answer; it is
///       tracked in <c>.agents/ideas/odata-provider-allowlist-upstream.md</c>, which is where to look before assuming
///       either class can be deleted.
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
      const string QUERY_STRING = "$filter=substring(Name, 1, 3) eq 'lic'";

      var failure = Record.Exception(() => Misconfigured(QUERY_STRING).RenderSql());

      failure.Should().BeOfType<QueryTranslationException>();

      // The guard is what breaks it: OData rewrites the call as a conditional over a helper of its own that has no SQL
      // translation. With the setting disabled the same filter is a plain Substring.
      failure!.Message.Should().Contain("ClrSafeFunctions");

      Apply(QUERY_STRING).RenderSql().Should().Contain("Substring(");
   }

   [Fact]
   public void Filter_WithAStringFunction_RendersANonSargableNullGuardWhenNullPropagationIsLeftAtItsDefault()
   {
      const string QUERY_STRING = "$filter=length(Name) eq 5";

      // A CASE over "column IS NULL" wrapped around the predicate: correct rows, but PostgreSQL cannot use an index
      // for it. On a column the model already declares non-nullable, the guard can never even fire.
      Misconfigured(QUERY_STRING).RenderSql().Should().ContainAll("CASE", "IS NULL");

      var configured = Apply(QUERY_STRING).RenderSql();

      configured.Should().NotContain("CASE");
      configured.Should().Contain("Length(");
   }

   [Fact]
   public async Task Filter_WithAStringFunction_ReturnsTheSameRowsEitherWay()
   {
      // The reason the misconfiguration is dangerous rather than merely slow: the rows are right, so nothing about the
      // response tells a consumer that anything is wrong.
      const string QUERY_STRING = "$filter=length(Name) eq 5";

      var misconfigured = await NamesAsync(Misconfigured(QUERY_STRING));
      var configured = await NamesAsync(Apply(QUERY_STRING));

      misconfigured.Should().BeEquivalentTo(configured);
      configured.Should().BeEquivalentTo("alice", "carol");
   }

   private AppliedQuery Misconfigured(string queryString)
   {
      return ODataQuery.Apply(Repository.Query(), queryString, ODataConfiguration.MisconfiguredQuerySettings);
   }
}
