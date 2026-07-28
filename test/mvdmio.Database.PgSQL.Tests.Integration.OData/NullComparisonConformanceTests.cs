using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    Inequality against a nullable column includes the rows where the column is null. This is externally visible
///    behaviour, not an implementation detail, and it is where an endpoint over this query surface differs from an
///    Entity Framework Core-backed one — which drops those rows.
/// </summary>
/// <remarks>
///    The behaviour here is the specified one: OData Part 2 §5.1.1.1 states that the null value is not equal to any
///    value but itself, so a row whose column is null does satisfy "not equal to 'bobby'". The query provider is in its
///    CLR-like null-comparison mode, kept deliberately per ADR 0004 because it matches both C# and the specification.
///    The cost is visible in the SQL: every inequality is widened with an "or the column is null" alternative, which
///    PostgreSQL cannot serve from an index.
/// </remarks>
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

   /// <remarks>
   ///    The counterpart to the nullable pins above, and the reason a generated mapping states nullability at all. Both
   ///    columns are <c>TEXT</c> and both are compared the same way; the only difference is that the table definition
   ///    types one of them non-nullable, which is enough for the widening to disappear. Nothing is lost by it: the null
   ///    alternative could never match a column that cannot hold null.
   /// </remarks>
   [Fact]
   public async Task Filter_WithInequalityOnANonNullableTextColumn_RendersNoNullAlternative()
   {
      var applied = Apply("$filter=Tier ne 'gold'");

      applied.RenderSql().Should().NotContain("IS NULL");

      (await NamesAsync(applied)).Should().BeEquivalentTo("bob", "dave");
   }
}
