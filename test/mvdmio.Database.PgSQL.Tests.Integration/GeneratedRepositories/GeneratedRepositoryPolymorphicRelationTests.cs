using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    Covers a Relation condition end to end, against the polymorphic-link fixture: a link table carrying a kind
///    column beside an identifier, reaching two different targets through that same pair, condition-narrowed rather
///    than through a generated column per kind — the shape the spec's problem statement describes.
/// </summary>
public class GeneratedRepositoryPolymorphicRelationTests : TestBase
{
   private readonly TestFixture _fixture;

   private PolymorphicLinkRepository _links = null!;
   private LinkPersonRepository _people = null!;
   private LinkAssetRepository _assets = null!;

   private long _bilboId;
   private long _swordId;
   private long _linkToBilboId;
   private long _linkToSwordId;

   public GeneratedRepositoryPolymorphicRelationTests(TestFixture fixture)
      : base(fixture)
   {
      _fixture = fixture;
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _links = new PolymorphicLinkRepository(Db);
      _people = new LinkPersonRepository(Db);
      _assets = new LinkAssetRepository(Db);

      _bilboId = (await _people.CreateAsync(new CreateLinkPersonCommand { Name = "bilbo" }, CancellationToken)).PersonId;
      _swordId = (await _assets.CreateAsync(new CreateLinkAssetCommand { Name = "sting" }, CancellationToken)).AssetId;

      // Both rows share the same target identifier, which is exactly why the kind column has to decide.
      _linkToBilboId = (await _links.CreateAsync(new CreatePolymorphicLinkCommand { Kind = LinkTargetKind.Person, TargetId = _bilboId }, CancellationToken)).LinkId;
      _linkToSwordId = (await _links.CreateAsync(new CreatePolymorphicLinkCommand { Kind = LinkTargetKind.Asset, TargetId = _swordId }, CancellationToken)).LinkId;
   }

   [Fact]
   public async Task Query_MaterializingTheConditionedRelation_ReturnsOnlyItsOwnKindsRow_InBothDirections()
   {
      var links = await _links.Query()
         .Include(x => x.Person)
         .Include(x => x.Asset)
         .OrderBy(x => x.LinkId)
         .ToListAsync(CancellationToken);

      var toBilbo = links.Single(x => x.LinkId == _linkToBilboId);
      var toSword = links.Single(x => x.LinkId == _linkToSwordId);

      toBilbo.Person!.Name.Should().Be("bilbo");
      toBilbo.Asset.Should().BeNull();

      toSword.Asset!.Name.Should().Be("sting");
      toSword.Person.Should().BeNull();

      // The reverse direction, declared on each target with the same class and the same kind of condition.
      var person = await _people.Query()
         .Include(x => x.Links)
         .Where(x => x.PersonId == _bilboId)
         .SingleAsync(CancellationToken);

      person.Links.Select(x => x.LinkId).Should().Equal(_linkToBilboId);

      var asset = await _assets.Query()
         .Include(x => x.Links)
         .Where(x => x.AssetId == _swordId)
         .SingleAsync(CancellationToken);

      asset.Links.Select(x => x.LinkId).Should().Equal(_linkToSwordId);
   }

   /// <remarks>
   ///    A row can be asked what it points at without knowing the kind first: both conditioned relations share the
   ///    same key pair (<c>TargetId</c> against each target's own primary key), and each still resolves independently.
   /// </remarks>
   [Fact]
   public async Task Query_IncludingSeveralConditionedRelationsSharingTheirPairs_ResolvesEachIndependently()
   {
      var link = await _links.Query()
         .Include(x => x.Person)
         .Include(x => x.Asset)
         .Where(x => x.LinkId == _linkToBilboId)
         .SingleAsync(CancellationToken);

      link.Person!.Name.Should().Be("bilbo");
      link.Asset.Should().BeNull();
   }

   [Fact]
   public async Task Query_FilteringAcrossTheConditionedRelation_ReachesOnlyTheMatchingKind()
   {
      var byPerson = await _links.Query()
         .Where(x => x.Person!.Name == "bilbo")
         .ToListAsync(CancellationToken);

      byPerson.Select(x => x.LinkId).Should().Equal(_linkToBilboId);

      // The same target identifier does not leak across kinds: the sword's target_id could collide with a person
      // row's own primary key and the condition is what keeps them apart.
      var byAsset = await _links.Query()
         .Where(x => x.Asset!.Name == "sting")
         .ToListAsync(CancellationToken);

      byAsset.Select(x => x.LinkId).Should().Equal(_linkToSwordId);
   }

   [Fact]
   public void Query_ReachingAConditionedRelationToOneRow_FoldsIntoASingleLeftJoin()
   {
      var sql = QueryDiagnostics.RenderSql(_links.Query().Select(x => x.Person!.Name));

      sql.Should().Contain("LEFT JOIN");
      sql.Should().NotContain("INNER JOIN");

      // One join per relation, not one per level — a single statement reaches the one row.
      System.Text.RegularExpressions.Regex.Matches(sql, "JOIN").Count.Should().Be(1);
   }

   /// <remarks>
   ///    The join carries plain column equality for the key pair, plus the condition, never an "or both are null"
   ///    alternative — which is what would cost a composite index on the real per-kind foreign key this fixture
   ///    stands in for.
   /// </remarks>
   [Fact]
   public void Query_TheRenderedSqlForAConditionedRelation_ShowsPlainEqualityPlusTheCondition()
   {
      var sql = QueryDiagnostics.RenderSql(_links.Query().Select(x => x.Person!.Name));

      sql.Should().MatchRegex(@"target_id[^\r\n]*=[^\r\n]*person_id");
      sql.Should().Contain("kind");
      sql.Should().NotContain("IS NULL");
   }

   /// <remarks>
   ///    Pins the one shortfall ADR 0010 records against user story 33, so that it stays a known limitation rather
   ///    than quietly becoming untrue. <c>Kind</c> is an enum, which this library maps with a value conversion by
   ///    default, and a comparison against a converted column binds the converted value as a parameter instead of
   ///    rendering it inline. Nothing forces it: <c>Sql.Constant</c> makes no difference in an association predicate,
   ///    and <c>Sql.ToSql</c> would push the enum's underlying number past the conversion and compare a text column
   ///    against <c>1</c>. If a future provider version renders this inline, this test fails and the ADR should be
   ///    revisited — that is the point of it.
   /// </remarks>
   [Fact]
   public void Query_AConditionComparingAConvertedColumn_StillBindsItsConstantAsAParameter()
   {
      var sql = QueryDiagnostics.RenderSql(_links.Query().Select(x => x.Person!.Name));

      sql.Should().NotContain("'Person'");
      sql.Should().MatchRegex(@"kind[^\r\n]*=[^\r\n]*[:@]\w+");
   }
}
