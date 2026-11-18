using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

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

      ((string?)claim.Note).Should().BeNull();
   }

   [Fact]
   public async Task Query_OverAColumnWhoseClaimTheTableDoesNotHonour_ReadsTheRowAnyway()
   {
      await InsertAsync(claimId: 2, label: null, note: "kept");

      var claim = await _claims.Query().Where(x => x.ClaimId == 2).SingleAsync(CancellationToken);

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
