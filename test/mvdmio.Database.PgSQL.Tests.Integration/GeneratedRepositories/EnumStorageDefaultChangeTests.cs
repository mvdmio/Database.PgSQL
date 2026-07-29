using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    The one behaviour change in this release that can break an existing consumer, pinned as breaking loudly. A
///    definition that does not claim its storage now maps its enum column as text, so a consumer relying on the query
///    surface's old default of storing the underlying number finds out from a failed query rather than from data that
///    quietly stopped matching.
/// </summary>
/// <remarks>
///    <see cref="UnclaimedIntegerEnumTable" /> maps the same <c>integer</c> column
///    <see cref="StorageClaimTable.Priority" /> claims, and claims nothing about it. The fix a consumer applies is exactly
///    the difference between the two definitions.
/// </remarks>
public class EnumStorageDefaultChangeTests : TestBase
{
   private UnclaimedIntegerEnumRepository _unclaimed = null!;
   private StorageClaimRepository _claimed = null!;

   public EnumStorageDefaultChangeTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _unclaimed = new UnclaimedIntegerEnumRepository(Db);
      _claimed = new StorageClaimRepository(Db);
   }

   [Fact]
   public async Task Query_OverAnIntegerColumnWhoseDefinitionDoesNotClaimIt_FailsLoudly()
   {
      await CreateRowAsync();

      var failure = await Record.ExceptionAsync(
         () => _unclaimed.Query().Where(x => x.Priority == WorkState.Closed).ToListAsync(CancellationToken)
      );

      failure.Should().NotBeNull();

      // The database's own complaint, not a mapping-time guess: text is what the column is now compared against and
      // integer is what it holds.
      var message = failure!.ToString();
      message.Should().Contain("integer").And.Contain("text");
   }

   [Fact]
   public async Task Query_OverTheSameColumnWithTheClaimStated_Succeeds()
   {
      var created = await CreateRowAsync();

      var matches = await _claimed.Query().Where(x => x.Priority == WorkState.Closed).ToListAsync(CancellationToken);

      matches.Select(x => x.ClaimId).Should().Equal(created.ClaimId);
   }

   /// <summary>
   ///    The Dapper surface is unaffected either way: an unclaimed enum has always been written as text there by the
   ///    registered handler, and the claim is what decides it now. Only the query surface's default moved.
   /// </summary>
   [Fact]
   public async Task Lookup_OverAnIntegerColumnWhoseDefinitionDoesNotClaimIt_StillReadsTheRow()
   {
      var created = await CreateRowAsync();

      var read = await _unclaimed.GetByPrimaryKeyAsync(created.ClaimId, CancellationToken);

      read.Should().NotBeNull();
      read!.Priority.Should().Be(WorkState.Closed);
   }

   private async Task<StorageClaimData> CreateRowAsync()
   {
      return await _claimed.CreateAsync(StorageClaimRows.Create(WorkState.Open, WorkState.Closed, reviewState: null), CancellationToken);
   }
}
