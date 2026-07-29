using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using NpgsqlTypes;
using System.Globalization;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    Covers a column's storage claim against a real table: what the database ends up holding, what comes back, and
///    whether the two surfaces agree. Every row of the documented matrix is exercised here, because each row is a promise
///    about a real column rather than about emitted text.
/// </summary>
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

   /// <summary>
   ///    Every cell of the documented matrix, read back through the query surface as well — so no row of it rests on the
   ///    Dapper side alone.
   /// </summary>
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

   /// <summary>
   ///    What each column of the matrix really holds, read as its own PostgreSQL type. This is the assertion that a claim
   ///    reached the wire rather than merely being carried in the mapping.
   /// </summary>
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

   /// <summary>
   ///    The one matrix cell where stating the claim changes the route to the database: a claimed dictionary is bound
   ///    through the parameter-type mechanism rather than through the process-wide Dapper conversion, so the value reaches
   ///    the column as a CLR dictionary rather than as a serialized string.
   /// </summary>
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

   /// <summary>
   ///    A claim naming the default has to be indistinguishable from claiming nothing, on both surfaces — otherwise the
   ///    matrix's "without a claim" column and its <c>Text</c> claim would be two different behaviours wearing one name.
   /// </summary>
   [Fact]
   public async Task ClaimingTheDefault_BehavesAsClaimingNothing()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.Open, reviewState: null);

      var byUnclaimed = await _repository.Query().Where(x => x.State == WorkState.Closed).ToListAsync(CancellationToken);
      var byClaimed = await _repository.Query().Where(x => x.Phase == WorkState.Closed).ToListAsync(CancellationToken);

      byUnclaimed.Select(x => x.ClaimId).Should().Equal(created.ClaimId);
      byClaimed.Select(x => x.ClaimId).Should().Equal(created.ClaimId);
   }

   /// <summary>
   ///    An unclaimed enum column holds the text of its member name, which is what this library has always promised and
   ///    what an application storing enums as text already has in its data.
   /// </summary>
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

   /// <summary>
   ///    The defect the claim exists to remove: the same column filtered through <c>Query()</c> the way it was written
   ///    through <c>CreateAsync</c>.
   /// </summary>
   [Fact]
   public async Task Query_FilteringAnUnclaimedEnumColumn_MatchesWhatCreateWrote()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.Open, reviewState: null);
      await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      var matches = await _repository.Query().Where(x => x.State == WorkState.Closed).ToListAsync(CancellationToken);

      matches.Select(x => x.ClaimId).Should().Equal(created.ClaimId);
   }

   /// <summary>
   ///    The predicate reaches the column directly, with the converted value in a parameter and no cast around either
   ///    side — so it can still use an index. The value is not in the SQL to assert on, which is what
   ///    <see cref="Query_FilteringAnUnclaimedEnumColumn_MatchesWhatCreateWrote" /> establishes instead: a row can only
   ///    match if the parameter carried the member name.
   /// </summary>
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

   /// <summary>
   ///    Two columns of one enum, stored two ways, read correctly through the same query. One table's choice does not
   ///    constrain the other's, and neither is a process-wide decision.
   /// </summary>
   [Fact]
   public async Task Query_OverBothEnumColumnsAtOnce_ReadsEachThroughItsOwnStorage()
   {
      var created = await CreateAsync(WorkState.Closed, WorkState.InProgress, reviewState: null);

      var read = await _repository.Query().Where(x => x.State == WorkState.Closed && x.Priority == WorkState.InProgress).SingleAsync(CancellationToken);

      read.ClaimId.Should().Be(created.ClaimId);
   }

   /// <summary>
   ///    Reading is what the two surfaces used to disagree about most quietly: a value that materialised through a lookup
   ///    would throw through a query. Both are asked for the same row here.
   /// </summary>
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

   /// <summary>
   ///    A stored value differing in case from the member name. Dapper's native text-to-enum path parses
   ///    case-insensitively, so the query surface's conversion does too rather than being stricter than the surface beside
   ///    it.
   /// </summary>
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

   /// <summary>
   ///    The <c>jsonb</c> case. PostgreSQL will not cast text to <c>jsonb</c> implicitly, so this used to fail every write
   ///    with <c>42804</c> while compiling clean.
   /// </summary>
   [Fact]
   public async Task StringOnAJsonbColumn_InsertsAndReadsBackAsAString()
   {
      var created = await CreateAsync(WorkState.Open, WorkState.Open, reviewState: null);

      created.Document.Should().Be("""{"kind": "invoice"}""");

      var read = await _repository.Query().Where(x => x.ClaimId == created.ClaimId).SingleAsync(CancellationToken);

      read.Document.Should().Be("""{"kind": "invoice"}""");
   }

   /// <summary>
   ///    That the value reached the column as JSON rather than as text. PostgreSQL reparses <c>jsonb</c> and answers with
   ///    its own formatting, so the whitespace the caller wrote is not what comes back — which a <c>text</c> column would
   ///    have returned verbatim.
   /// </summary>
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

      // Read as jsonb rather than as text, so the assertion is that the column really holds JSON rather than a string
      // that happens to look like it.
      var kind = await Db.Dapper.QuerySingleAsync<string>(
         "SELECT document ->> 'kind' FROM public.generated_storage_claims WHERE claim_id = :claimId",
         new Dictionary<string, object?> { ["claimId"] = created.ClaimId },
         ct: CancellationToken
      );

      kind.Should().Be("credit-note");
   }

   /// <summary>
   ///    Load-bearing and previously unverified: the <c>jsonb</c> binding works by putting an
   ///    <c>ICustomQueryParameter</c> into the parameter dictionary generated code builds, and nothing had established
   ///    that Dapper honours one when it arrives that way rather than on a parameter object.
   /// </summary>
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

   /// <summary>
   ///    The row the matrix calls load-bearing: an application with JSON in <c>text</c> columns that it casts at query
   ///    time must keep binding those as text, so fixing <c>jsonb</c> cannot break them.
   /// </summary>
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

   /// <summary>
   ///    The generated data type keeps a <c>[Generated]</c> column non-publicly settable, and both surfaces still populate
   ///    it. That the provider can write a private setter at all is the part this settles — it is undocumented, and the
   ///    alternative would have been a backing-field escape hatch.
   /// </summary>
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

   /// <summary>
   ///    The evidence behind <c>PGSQL0022</c>. A storage claim states how the column is represented; it does not ask for a
   ///    conversion, and the driver refuses a string bound under an integral type outright.
   /// </summary>
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

   /// <summary>
   ///    What hand-written Dapper SQL does with an enum parameter, unchanged by this release and unchanged by whether the
   ///    type handlers are registered: it binds the number behind the member.
   /// </summary>
   /// <remarks>
   ///    The registration does not reach this, and never did — Dapper resolves an enum parameter to its underlying type
   ///    before it consults the handler table, so <c>EnumAsStringTypeHandler.SetValue</c> is unreachable from the parameter
   ///    path. Pinned because the registration reads as though it settled both directions, and because it is the reason a
   ///    generated repository states the conversion at its own binding site rather than relying on a registry. A
   ///    hand-written statement that wants the member name binds the string itself, as the second half shows.
   /// </remarks>
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

   /// <summary>
   ///    That an opt-in convenience cannot change what a generated repository does. The registered handler stores this
   ///    enum as text, and the integer-claimed column is written and read correctly all the same — which it could not be
   ///    if the handler had intercepted the binding.
   /// </summary>
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

   /// <summary>What the column really holds, read as its own PostgreSQL type rather than through the mapping.</summary>
   private async Task<T> ReadColumnAsync<T>(string columnName, long claimId)
   {
      return await Db.Dapper.QuerySingleAsync<T>(
         $"SELECT {columnName} FROM public.generated_storage_claims WHERE claim_id = :claimId",
         new Dictionary<string, object?> { ["claimId"] = claimId },
         ct: CancellationToken
      );
   }
}
