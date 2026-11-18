using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using NpgsqlTypes;
using System.Globalization;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

public class GeneratedRepositoryStorageTests : TestBase
{
   private StorageClaimRepository _repository = null!;

   public GeneratedRepositoryStorageTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _repository = new StorageClaimRepository(Db);
   }

   [Fact]
   public async Task Create_ThenRead_RoundTripsEveryStorageShape()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.InProgress, WorkState.Open);

      var read = await _repository.GetByPrimaryKeyAsync(created.ClaimId, CancellationToken);

      read.Should().NotBeNull();
      read!.State.Should().Be(WorkState.Closed);
      read.Phase.Should().Be(WorkState.Closed);
      read.Priority.Should().Be(WorkState.InProgress);
      read.Severity.Should().Be(WorkState.InProgress);
      read.Epoch.Should().Be(WorkState.InProgress);
      read.ReviewState.Should().Be(WorkState.Open);
      read.ReviewPriority.Should().Be(WorkState.Open);
      read.Document.Should().Be("""{"kind": "invoice"}""");
      read.Draft.Should().Be("""{"kind": "draft"}""");
      read.LegacyDocument.Should().Be("""{"kind":"legacy"}""");
      read.PlainNote.Should().Be("a plain note");
      read.OffsetHours.Should().Be(-3);
      read.OffsetClaimed.Should().Be(-3);
      read.OptionalOffset.Should().BeNull();
      read.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "gold" });
      read.ClaimedMetadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "silver" });
      read.JsonMetadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "bronze" });
   }

   [Fact]
   public async Task Query_OverEveryStorageShape_ReadsBackWhatCreateWrote()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.InProgress, WorkState.Open);

      var read = await _repository.Query().Where(x => x.ClaimId == created.ClaimId).SingleAsync(CancellationToken);

      read.State.Should().Be(WorkState.Closed);
      read.Phase.Should().Be(WorkState.Closed);
      read.Priority.Should().Be(WorkState.InProgress);
      read.Severity.Should().Be(WorkState.InProgress);
      read.Epoch.Should().Be(WorkState.InProgress);
      read.ReviewState.Should().Be(WorkState.Open);
      read.ReviewPriority.Should().Be(WorkState.Open);
      read.Document.Should().Be("""{"kind": "invoice"}""");
      read.Draft.Should().Be("""{"kind": "draft"}""");
      read.LegacyDocument.Should().Be("""{"kind":"legacy"}""");
      read.PlainNote.Should().Be("a plain note");
      read.OffsetHours.Should().Be(-3);
      read.OffsetClaimed.Should().Be(-3);
      read.OptionalOffset.Should().BeNull();
      read.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "gold" });
      read.ClaimedMetadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "silver" });
      read.JsonMetadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "bronze" });
   }

   [Theory]
   [InlineData("state", "Closed")]
   [InlineData("phase", "Closed")]
   [InlineData("review_state", "Open")]
   [InlineData("plain_note", "a plain note")]
   [InlineData("legacy_document", """{"kind":"legacy"}""")]
   public async Task TextColumn_HoldsTheTextItsClaimSettles(string columnName, string expected)
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.InProgress, WorkState.Open);

      (await ReadColumnAsync<string>(columnName, created.ClaimId)).Should().Be(expected);
   }

   [Theory]
   [InlineData("priority")]
   [InlineData("severity")]
   [InlineData("epoch")]
   [InlineData("review_priority")]
   public async Task NumericallyClaimedEnumColumn_HoldsTheUnderlyingNumber(string columnName)
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.InProgress, WorkState.InProgress);

      (await ReadColumnAsync<long>(columnName, created.ClaimId)).Should().Be((long)WorkState.InProgress);
   }

   [Theory]
   [InlineData("metadata", "gold")]
   [InlineData("claimed_metadata", "silver")]
   [InlineData("json_metadata", "bronze")]
   public async Task DictionaryColumn_HoldsJsonWhicheverRouteItTook(string columnName, string expected)
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      var tier = await Db.Dapper.QuerySingleAsync<string>(
         $"SELECT {columnName} ->> 'tier' FROM public.generated_storage_claims WHERE claim_id = :claimId",
         new Dictionary<string, object?> { ["claimId"] = created.ClaimId },
         ct: CancellationToken
      );

      tier.Should().Be(expected);
   }

   [Theory]
   [InlineData("offset_hours")]
   [InlineData("offset_claimed")]
   public async Task SignedByteColumn_HoldsTheWidenedNumber(string columnName)
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      (await ReadColumnAsync<short>(columnName, created.ClaimId)).Should().Be(-3);
   }

   [Fact]
   public async Task NullableSignedByteColumn_RoundTripsAValueAndANull()
   {
      var withNull = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      var update = StorageClaimRows.UpdateFrom(withNull);
      update.OptionalOffset = -9;

      var withValue = await _repository.UpdateAsync(update, CancellationToken);

      withValue.OptionalOffset.Should().Be(-9);
      (await ReadColumnAsync<short?>("optional_offset", withNull.ClaimId)).Should().Be(-9);

      var throughQuery = await _repository.Query().Where(x => x.OptionalOffset == (sbyte)-9).SingleAsync(CancellationToken);

      throughQuery.ClaimId.Should().Be(withNull.ClaimId);
   }

   [Fact]
   public async Task ClaimingTheDefault_BehavesAsClaimingNothing()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.Open, reviewState: null);

      var byUnclaimed = await _repository.Query().Where(x => x.State == WorkState.Closed).ToListAsync(CancellationToken);
      var byClaimed = await _repository.Query().Where(x => x.Phase == WorkState.Closed).ToListAsync(CancellationToken);

      byUnclaimed.Select(x => x.ClaimId).Should().Equal(created.ClaimId);
      byClaimed.Select(x => x.ClaimId).Should().Equal(created.ClaimId);
   }

   [Fact]
   public async Task UnclaimedEnumColumn_HoldsTheMemberNameAsText()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.Open, reviewState: null);

      var stored = await ReadColumnAsync<string>("state", created.ClaimId);

      stored.Should().Be("Closed");
   }

   [Fact]
   public async Task EnumColumnClaimedAsAnInteger_HoldsTheUnderlyingNumber()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Closed, reviewState: null);

      var stored = await ReadColumnAsync<int>("priority", created.ClaimId);

      stored.Should().Be((int)WorkState.Closed);
   }

   [Fact]
   public async Task Query_FilteringAnUnclaimedEnumColumn_MatchesWhatCreateWrote()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.Open, reviewState: null);
      await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      var matches = await _repository.Query().Where(x => x.State == WorkState.Closed).ToListAsync(CancellationToken);

      matches.Select(x => x.ClaimId).Should().Equal(created.ClaimId);
   }

   [Fact]
   public void Query_FilteringAnUnclaimedEnumColumn_ComparesTheColumnAgainstAParameter()
   {
      var sql = QueryDiagnostics.RenderSql(_repository.Query().Where(x => x.State == WorkState.Closed));

      sql.Should().Contain("x.state = :State");
      sql.Should().NotContain("::");
   }

   [Fact]
   public async Task Query_FilteringAnIntegerClaimedEnumColumn_MatchesWhatCreateWrote()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Closed, reviewState: null);
      await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      var matches = await _repository.Query().Where(x => x.Priority == WorkState.Closed).ToListAsync(CancellationToken);

      matches.Select(x => x.ClaimId).Should().Equal(created.ClaimId);
   }

   [Fact]
   public async Task Query_OverBothEnumColumnsAtOnce_ReadsEachThroughItsOwnStorage()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.InProgress, reviewState: null);

      var read = await _repository.Query().Where(x => x.State == WorkState.Closed && x.Priority == WorkState.InProgress).SingleAsync(CancellationToken);

      read.ClaimId.Should().Be(created.ClaimId);
   }

   [Fact]
   public async Task EnumColumn_ReadsIdenticallyThroughBothSurfaces()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.InProgress, WorkState.Open);

      var throughDapper = await _repository.GetByPrimaryKeyAsync(created.ClaimId, CancellationToken);
      var throughQuery = await _repository.Query().Where(x => x.ClaimId == created.ClaimId).SingleAsync(CancellationToken);

      throughQuery.State.Should().Be(throughDapper!.State);
      throughQuery.Priority.Should().Be(throughDapper.Priority);
      throughQuery.ReviewState.Should().Be(throughDapper.ReviewState);
   }

   [Fact]
   public async Task EnumColumn_HoldingADifferentlyCasedMemberName_ReadsThroughBothSurfaces()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.Open, reviewState: null);
      await Db.Dapper.ExecuteAsync(
         "UPDATE public.generated_storage_claims SET state = 'cLOSED' WHERE claim_id = :claimId",
         new Dictionary<string, object?> { ["claimId"] = created.ClaimId },
         ct: CancellationToken
      );

      var throughDapper = await _repository.GetByPrimaryKeyAsync(created.ClaimId, CancellationToken);
      var throughQuery = await _repository.Query().Where(x => x.ClaimId == created.ClaimId).SingleAsync(CancellationToken);

      throughDapper!.State.Should().Be(WorkState.Closed);
      throughQuery.State.Should().Be(WorkState.Closed);
   }

   [Fact]
   public async Task NullableEnumColumn_HoldingNull_ReadsBackAsNullThroughBothSurfaces()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      var throughDapper = await _repository.GetByPrimaryKeyAsync(created.ClaimId, CancellationToken);
      var throughQuery = await _repository.Query().Where(x => x.ClaimId == created.ClaimId).SingleAsync(CancellationToken);

      throughDapper!.ReviewState.Should().BeNull();
      throughQuery.ReviewState.Should().BeNull();
      await ReadColumnAsync<string?>("review_state", created.ClaimId).ContinueWith(x => x.Result.Should().BeNull(), CancellationToken);
   }

   [Fact]
   public async Task StringOnAJsonbColumn_InsertsAndReadsBackAsAString()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      created.Document.Should().Be("""{"kind": "invoice"}""");

      var read = await _repository.Query().Where(x => x.ClaimId == created.ClaimId).SingleAsync(CancellationToken);

      read.Document.Should().Be("""{"kind": "invoice"}""");
   }

   [Fact]
   public async Task StringOnAJsonbColumn_IsReformattedByPostgresRatherThanStoredVerbatim()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      created.Document.Should().Be("""{"kind": "invoice"}""");
   }

   [Fact]
   public async Task StringOnAJsonbColumn_UpdatesSuccessfully()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      var update = StorageClaimRows.UpdateFrom(created);
      update.Document = """{"kind": "credit-note"}""";

      var updated = await _repository.UpdateAsync(update, CancellationToken);

      updated.Document.Should().Be("""{"kind": "credit-note"}""");

      var kind = await Db.Dapper.QuerySingleAsync<string>(
         "SELECT document ->> 'kind' FROM public.generated_storage_claims WHERE claim_id = :claimId",
         new Dictionary<string, object?> { ["claimId"] = created.ClaimId },
         ct: CancellationToken
      );

      kind.Should().Be("credit-note");
   }

   [Fact]
   public async Task CustomQueryParameter_InsideTheParameterDictionary_IsHonouredByDapper()
   {
      var kind = await Db.Dapper.QuerySingleAsync<string>(
         "SELECT (:document::JSONB) ->> 'kind'",
         new Dictionary<string, object?> { ["document"] = new TypedQueryParameter("""{"kind": "invoice"}""", NpgsqlDbType.Jsonb) },
         ct: CancellationToken
      );

      kind.Should().Be("invoice");
   }

   [Fact]
   public async Task CustomQueryParameter_CarryingNull_BindsANullParameter()
   {
      var value = await Db.Dapper.QuerySingleAsync<string?>(
         "SELECT :document::JSONB #>> '{}'",
         new Dictionary<string, object?> { ["document"] = new TypedQueryParameter(null, NpgsqlDbType.Jsonb) },
         ct: CancellationToken
      );

      value.Should().BeNull();
   }

   [Fact]
   public async Task UnclaimedStringColumn_KeepsBindingAsText()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      var stored = await ReadColumnAsync<string>("legacy_document", created.ClaimId);

      stored.Should().Be("""{"kind":"legacy"}""");

      var castAtQueryTime = await Db.Dapper.QuerySingleAsync<string>(
         "SELECT (legacy_document::JSONB) ->> 'kind' FROM public.generated_storage_claims WHERE claim_id = :claimId",
         new Dictionary<string, object?> { ["claimId"] = created.ClaimId },
         ct: CancellationToken
      );

      castAtQueryTime.Should().Be("legacy");
   }

   [Fact]
   public async Task DictionaryColumn_KeepsMappingToJsonbWithNoClaim()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      var tier = await Db.Dapper.QuerySingleAsync<string>(
         "SELECT metadata ->> 'tier' FROM public.generated_storage_claims WHERE claim_id = :claimId",
         new Dictionary<string, object?> { ["claimId"] = created.ClaimId },
         ct: CancellationToken
      );

      tier.Should().Be("gold");

      var read = await _repository.Query().Where(x => x.ClaimId == created.ClaimId).SingleAsync(CancellationToken);

      read.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "gold" });
   }

   [Fact]
   public async Task SignedByteColumn_IsWritableAndReadsBackThroughBothSurfaces()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      created.OffsetHours.Should().Be(-3);

      var throughQuery = await _repository.Query().Where(x => x.OffsetHours == (sbyte)-3).SingleAsync(CancellationToken);

      throughQuery.ClaimId.Should().Be(created.ClaimId);
   }

   [Fact]
   public async Task GeneratedColumnWithAPrivateSetter_MaterializesThroughBothSurfaces()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      created.ClaimId.Should().BeGreaterThan(0);
      created.CreatedAt.Should().NotBe(default);

      var throughQuery = await _repository.Query().Where(x => x.ClaimId == created.ClaimId).SingleAsync(CancellationToken);

      throughQuery.ClaimId.Should().Be(created.ClaimId);
      throughQuery.CreatedAt.Should().BeCloseTo(created.CreatedAt, TimeSpan.FromSeconds(5));
   }

   [Theory]
   [InlineData(NpgsqlDbType.Smallint)]
   [InlineData(NpgsqlDbType.Integer)]
   [InlineData(NpgsqlDbType.Bigint)]
   public async Task StringBoundUnderAnIntegralType_IsRefusedByTheDriver(NpgsqlDbType claim)
   {
      var failure = await Record.ExceptionAsync(
         () => Db.Dapper.QuerySingleAsync<string>(
            "SELECT :value::TEXT",
            new Dictionary<string, object?> { ["value"] = new TypedQueryParameter("42", claim) },
            ct: CancellationToken
         )
      );

      failure.Should().BeOfType<QueryException>();
      failure!.InnerException!.Message.Should().Contain("System.String");
   }

   [Fact]
   public async Task HandWrittenSql_BindsAnEnumParameterAsItsUnderlyingNumber()
   {
      var byInference = await Db.Dapper.QuerySingleAsync<string>(
         "SELECT :state::TEXT",
         new Dictionary<string, object?> { ["state"] = WorkState.InProgress },
         ct: CancellationToken
      );

      byInference.Should().Be(((int)WorkState.InProgress).ToString(CultureInfo.InvariantCulture));

      var asStated = await Db.Dapper.QuerySingleAsync<string>(
         "SELECT :state::TEXT",
         new Dictionary<string, object?> { ["state"] = WorkState.InProgress.ToString() },
         ct: CancellationToken
      );

      asStated.Should().Be("InProgress");
   }

   [Fact]
   public async Task RegisteredEnumTypeHandler_DoesNotReachAGeneratedRepositorysBinding()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Closed, reviewState: null);

      var stored = await ReadColumnAsync<int>("priority", created.ClaimId);

      stored.Should().Be((int)WorkState.Closed);
   }

   private async Task<StorageClaimData> CreateAsync(WorkState state, WorkState priority, WorkState? reviewState)
   {
      return await _repository.CreateAsync(StorageClaimRows.Create(state, priority, reviewState), CancellationToken);
   }

   private async Task<T> ReadColumnAsync<T>(string columnName, long claimId)
   {
      return await Db.Dapper.QuerySingleAsync<T>(
         $"SELECT {columnName} FROM public.generated_storage_claims WHERE claim_id = :claimId",
         new Dictionary<string, object?> { ["claimId"] = claimId },
         ct: CancellationToken
      );
   }
}
