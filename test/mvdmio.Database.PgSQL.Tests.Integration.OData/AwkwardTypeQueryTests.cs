using AwesomeAssertions;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData;

/// <summary>
///    The other half of what <see cref="GeneratedTypeModelTests" /> asks. That one records what an OData model makes of
///    the awkward property types; this one records what the query surface does with them, so that a limitation can be
///    attributed to the right layer instead of being blamed on whichever one was looked at first.
/// </summary>
public class AwkwardTypeQueryTests : ODataTestBase
{
   private AwkwardRepository _repository = null!;

   public AwkwardTypeQueryTests(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _repository = new AwkwardRepository(Db);
   }

   [Fact]
   public async Task Query_OverEveryAwkwardType_ReadsTheValuesBackThroughTheGeneratedMapping()
   {
      var created = await InsertAwkwardRowAsync();

      var read = await _repository.Query().Where(x => x.AwkwardId == created.AwkwardId).SingleAsync(CancellationToken);

      read.HomePage.Should().Be(new Uri("https://example.com/awkward"));
      read.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "gold" });
      read.BirthDate.Should().Be(new DateOnly(1990, 2, 3));
      read.WakeTime.Should().Be(new TimeOnly(7, 15));
      read.Duration.Should().Be(TimeSpan.FromMinutes(90));
      read.Payload.Should().Equal((byte)1, (byte)2, (byte)3);
      read.Initial.Should().Be('a');
      read.SignedOffset.Should().Be(-3);
      read.OccurredAt.Should().Be(new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Unspecified));
   }

   [Fact]
   public async Task Query_FilteringOnTheAwkwardTypes_TranslatesToSql()
   {
      await InsertAwkwardRowAsync();

      var query = _repository.Query().Where(x => x.BirthDate > new DateOnly(1980, 1, 1) && x.Initial == 'a');

      var matches = await query.ToListAsync(CancellationToken);

      matches.Should().ContainSingle();
      matches[0].SignedOffset.Should().Be(-3);
   }

   /// <summary>
   ///    A signed byte is written through the generated repository, which it could not be before the storage claim
   ///    existed. Nothing declares the claim: the widening is what an unclaimed <c>sbyte</c> gets, because widening is all
   ///    it ever needed.
   /// </summary>
   [Fact]
   public async Task Create_OnATableCarryingASignedByte_WritesTheRow()
   {
      var created = await _repository.CreateAsync(
         new CreateAwkwardCommand
         {
            BirthDate = new DateOnly(1985, 6, 15),
            WakeTime = new TimeOnly(8, 30),
            Duration = TimeSpan.FromMinutes(30),
            Initial = 'b',
            SignedOffset = -7,
            OccurredAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
         },
         CancellationToken
      );

      created.SignedOffset.Should().Be(-7);

      var read = await _repository.Query().Where(x => x.SignedOffset == (sbyte)-7).SingleAsync(CancellationToken);

      read.AwkwardId.Should().Be(created.AwkwardId);
   }

   /// <summary>
   ///    Why the claim is applied where the value is bound rather than left to inference. The driver has no mapping for
   ///    the <c>DbType</c> Dapper infers from a signed byte, so a bare parameter is refused — and stating the PostgreSQL
   ///    type on the same value is the whole of the fix a generated repository now applies for itself.
   /// </summary>
   [Fact]
   public async Task Parameter_CarryingASignedByte_IsRefusedByInferenceAndAcceptedWithAStatedType()
   {
      var inferred = await Record.ExceptionAsync(
         () => Db.Dapper.QuerySingleAsync<string>(
            "SELECT :value::TEXT",
            new Dictionary<string, object?> { ["value"] = (sbyte)-3 },
            ct: CancellationToken
         )
      );

      inferred.Should().BeOfType<QueryException>();
      inferred!.InnerException.Should().BeOfType<NotSupportedException>();
      inferred.InnerException!.Message.Should().Contain("SByte");

      var stated = await Db.Dapper.QuerySingleAsync<string>(
         "SELECT :value::TEXT",
         new Dictionary<string, object?> { ["value"] = new TypedQueryParameter((sbyte)-3, NpgsqlDbType.Smallint) },
         ct: CancellationToken
      );

      stated.Should().Be("-3");
   }

   /// <summary>
   ///    The evidence behind <c>PGSQL0023</c>. These three widths are refused as column types now, and this is why: the
   ///    driver has no mapping for any of them, so no statement can carry one however the parameter is built.
   /// </summary>
   [Theory]
   [InlineData("UInt16", (ushort)42)]
   [InlineData("UInt32", 4_000_000_000U)]
   [InlineData("UInt64", 9_000_000_000_000_000_000UL)]
   public async Task Parameter_OfAnUnsignedInteger_ReportsTheUnsupportedDbType(string dbType, object value)
   {
      var failure = await Record.ExceptionAsync(
         () => Db.Dapper.QuerySingleAsync<string>(
            "SELECT :value::TEXT",
            new Dictionary<string, object?> { ["value"] = value },
            ct: CancellationToken
         )
      );

      failure.Should().BeOfType<QueryException>();
      failure!.InnerException!.Message.Should().Contain(dbType);
   }

   private async Task<AwkwardData> InsertAwkwardRowAsync()
   {
      return await _repository.CreateAsync(
         new CreateAwkwardCommand
         {
            HomePage = new Uri("https://example.com/awkward"),
            Metadata = new Dictionary<string, string> { ["tier"] = "gold" },
            BirthDate = new DateOnly(1990, 2, 3),
            WakeTime = new TimeOnly(7, 15),
            Duration = TimeSpan.FromMinutes(90),
            Payload = [1, 2, 3],
            Initial = 'a',
            SignedOffset = -3,
            OccurredAt = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Unspecified)
         },
         CancellationToken
      );
   }
}
