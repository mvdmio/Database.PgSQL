using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

public class NullComparisonConformanceTests : SampleConformanceTestBase
{
   public NullComparisonConformanceTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public async Task Filter_WithInequalityOnANullableTextColumn_IncludesTheNullRows()
   {
      var applied = Apply("$filter=Nickname ne 'bobby'");

      applied.RenderSql().Should().Contain("IS NULL");

      (await NamesAsync(applied)).Should().BeEquivalentTo("alice", "carol", "dave");
   }

   [Fact]
   public async Task Filter_WithInequalityOnANullableNumericColumn_IncludesTheNullRows()
   {
      var applied = Apply("$filter=Bonus ne 2");

      applied.RenderSql().Should().Contain("IS NULL");

      (await NamesAsync(applied)).Should().BeEquivalentTo("alice", "carol");
   }

   [Fact]
   public async Task Filter_WithAnExplicitNullComparison_SelectsExactlyTheNullRows()
   {
      (await NamesAsync(Apply("$filter=Nickname eq null"))).Should().BeEquivalentTo("alice", "dave");
      (await NamesAsync(Apply("$filter=Nickname ne null"))).Should().BeEquivalentTo("bob", "carol");
   }

   [Fact]
   public async Task Filter_WithEqualityOnANullableColumn_ExcludesTheNullRows()
   {
      (await NamesAsync(Apply("$filter=Nickname eq 'bobby'"))).Should().Equal("bob");
   }

   [Fact]
   public async Task Filter_WithInequalityOnANonNullableTextColumn_RendersNoNullAlternative()
   {
      var applied = Apply("$filter=Tier ne 'gold'");

      applied.RenderSql().Should().NotContain("IS NULL");

      (await NamesAsync(applied)).Should().BeEquivalentTo("bob", "dave");
   }
}
