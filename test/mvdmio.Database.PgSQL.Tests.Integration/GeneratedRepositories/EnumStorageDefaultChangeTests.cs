using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

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
