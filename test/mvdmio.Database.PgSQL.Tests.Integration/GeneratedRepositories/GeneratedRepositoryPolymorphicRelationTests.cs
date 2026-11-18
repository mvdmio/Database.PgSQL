using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

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

      System.Text.RegularExpressions.Regex.Matches(sql, "JOIN").Count.Should().Be(1);
   }

   [Fact]
   public void Query_TheRenderedSqlForAConditionedRelation_ShowsPlainEqualityPlusTheCondition()
   {
      var sql = QueryDiagnostics.RenderSql(_links.Query().Select(x => x.Person!.Name));

      sql.Should().MatchRegex(@"target_id[^\r\n]*=[^\r\n]*person_id");
      sql.Should().Contain("kind");
      sql.Should().NotContain("IS NULL");
   }

   [Fact]
   public void Query_AConditionComparingAConvertedColumn_StillBindsItsConstantAsAParameter()
   {
      var sql = QueryDiagnostics.RenderSql(_links.Query().Select(x => x.Person!.Name));

      sql.Should().NotContain("'Person'");
      sql.Should().MatchRegex(@"kind[^\r\n]*=[^\r\n]*[:@]\w+");
   }
}
