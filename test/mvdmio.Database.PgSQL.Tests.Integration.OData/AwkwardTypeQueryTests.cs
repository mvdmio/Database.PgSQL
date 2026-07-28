using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

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
      var awkwardId = await InsertAwkwardRowAsync();

      var read = await _repository.Query().Where(x => x.AwkwardId == awkwardId).SingleAsync(CancellationToken);

      read.HomePage.Should().Be(new Uri("https://example.com/awkward"));
      read.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "gold" });
      read.BirthDate.Should().Be(new DateOnly(1990, 2, 3));
      read.WakeTime.Should().Be(new TimeOnly(7, 15));
      read.Duration.Should().Be(TimeSpan.FromMinutes(90));
      read.Payload.Should().Equal((byte)1, (byte)2, (byte)3);
      read.Initial.Should().Be('a');
      read.SignedOffset.Should().Be(-3);
      read.SmallCount.Should().Be(42);
      read.MediumCount.Should().Be(4_000_000_000);
      read.OccurredAt.Should().Be(new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Unspecified));

      // Above long.MaxValue, so it reads back fine here but cannot be represented in an OData model at all — see
      // GeneratedTypeModelTests.
      read.LargeCount.Should().Be(9_000_000_000_000_000_000);
   }

   [Fact]
   public async Task Query_FilteringOnTheAwkwardTypes_TranslatesToSql()
   {
      await InsertAwkwardRowAsync();

      var query = _repository.Query().Where(x => x.BirthDate > new DateOnly(1980, 1, 1) && x.Initial == 'a');

      var matches = await query.ToListAsync(CancellationToken);

      matches.Should().ContainSingle();
      matches[0].SmallCount.Should().Be(42);
   }

   /// <summary>
   ///    A characterization test, and a finding rather than a design: the generator's mappable-type allowlist admits the
   ///    signed byte and all three unsigned integer widths, but the PostgreSQL driver has no mapping for their
   ///    <c>DbType</c>s, so a row carrying any of them can never be written through the generated repository. Reading
   ///    works, which is why the tests above can still cover the types. Tracked in
   ///    <c>.agents/ideas/generator-driver-unsupported-numeric-types.md</c>.
   /// </summary>
   [Fact]
   public async Task Create_OnATableCarryingASignedByteOrUnsignedInteger_IsRefusedByTheDriver()
   {
      var failure = await Record.ExceptionAsync(
         () => _repository.CreateAsync(
            new CreateAwkwardCommand
            {
               BirthDate = new DateOnly(1985, 6, 15),
               WakeTime = new TimeOnly(8, 30),
               Duration = TimeSpan.FromMinutes(30),
               Initial = 'b',
               SignedOffset = 1,
               SmallCount = 1,
               MediumCount = 1,
               LargeCount = 1,
               OccurredAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
            },
            CancellationToken
         )
      );

      failure.Should().BeOfType<QueryException>();
      failure!.InnerException.Should().BeOfType<NotSupportedException>();
      failure.InnerException!.Message.Should().Contain("SByte", "the signed byte is the first of the four the driver reaches");
   }

   [Theory]
   [InlineData("SByte", (sbyte)-3)]
   [InlineData("UInt16", (ushort)42)]
   [InlineData("UInt32", 4_000_000_000U)]
   [InlineData("UInt64", 9_000_000_000_000_000_000UL)]
   public async Task Parameter_OfAnAllowlistedTypeTheDriverRejects_ReportsTheUnsupportedDbType(string dbType, object value)
   {
      // Isolated per type, because the create above stops at the first one. Nothing OData-specific: the refusal is in
      // the driver, below both the query surface and the front end.
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

   /// <remarks>
   ///    Written as literal SQL rather than through <c>CreateAsync</c> because of the signed-byte refusal pinned above.
   /// </remarks>
   private async Task<long> InsertAwkwardRowAsync()
   {
      return await Db.Dapper.QuerySingleAsync<long>(
         """
         INSERT INTO public.odata_awkward (
            home_page, metadata, birth_date, wake_time, duration, payload,
            initial, signed_offset, small_count, medium_count, large_count, occurred_at
         )
         VALUES (
            'https://example.com/awkward', '{"tier":"gold"}'::JSONB, DATE '1990-02-03', TIME '07:15', INTERVAL '90 minutes', '\x010203'::BYTEA,
            'a', -3, 42, 4000000000, 9000000000000000000, TIMESTAMP '2024-05-06 07:08:09'
         )
         RETURNING awkward_id
         """,
         ct: CancellationToken
      );
   }
}
