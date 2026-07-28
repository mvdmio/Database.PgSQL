using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    What a nullability claim buys and what it costs, over a table whose columns all permit null.
/// </summary>
/// <remarks>
///    A claim is never verified against the real table, so this is where the accepted risk is pinned rather than
///    described. Both halves are provider behaviour, asserted here rather than reasoned about, because an upgrade could
///    change either: a wrong claim is not caught when the row is read — the null arrives in a property typed
///    non-nullable — and what it does cost is a row set, because the null alternative it removed is what would have
///    matched the null rows.
/// </remarks>
public class GeneratedRepositoryNullabilityTests : TestBase
{
   private UnverifiedClaimRepository _claims = null!;

   public GeneratedRepositoryNullabilityTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _claims = new UnverifiedClaimRepository(Db);
   }

   [Fact]
   public async Task Query_OverAColumnWhoseClaimIsWithdrawn_ReadsTheNullBack()
   {
      await InsertAsync(claimId: 1, label: "kept", note: null);

      var claim = await _claims.Query().Where(x => x.ClaimId == 1).SingleAsync(CancellationToken);

      claim.Label.Should().Be("kept");

      // Typed non-nullable, and holding null anyway: withdrawing the claim is what lets the read complete at all, and
      // the property type is not what the value has to satisfy.
      ((string?)claim.Note).Should().BeNull();
   }

   [Fact]
   public async Task Query_OverAColumnWhoseClaimTheTableDoesNotHonour_ReadsTheRowAnyway()
   {
      await InsertAsync(claimId: 2, label: null, note: "kept");

      var claim = await _claims.Query().Where(x => x.ClaimId == 2).SingleAsync(CancellationToken);

      // The read path does not enforce the claim, so a wrong one is not caught here.
      ((string?)claim.Label).Should().BeNull();
   }

   [Fact]
   public async Task Query_WithInequalityOverAColumnWhoseClaimTheTableDoesNotHonour_OmitsTheNullRows()
   {
      await InsertAsync(claimId: 3, label: null, note: "kept");
      await InsertAsync(claimId: 4, label: "kept", note: "kept");

      var labels = await _claims.Query()
         .Where(x => x.ClaimId >= 3 && x.Label != "kept")
         .ToListAsync(CancellationToken);

      // This is what a wrong claim actually costs, and it is why the claim is a statement about the table rather than a
      // hint: dropping the null alternative drops the rows the alternative would have matched.
      labels.Should().BeEmpty();
   }

   private async Task InsertAsync(long claimId, string? label, string? note)
   {
      await Db.Dapper.ExecuteAsync(
         """
         INSERT INTO public.generated_unverified_claims (claim_id, label, note)
         VALUES (@claimId, @label, @note)
         """,
         new Dictionary<string, object?>
         {
            ["claimId"] = claimId,
            ["label"] = label,
            ["note"] = note
         },
         ct: CancellationToken
      );
   }
}
