using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    End-to-end cover for a Relation key whose target column is <c>[Unique]</c> and nullable: the fixture's real
///    <c>UNIQUE</c> constraint admits any number of nulls, the emitted join stays plain equality rather than widening
///    into "equal, or both are null", and materializing the relation reaches exactly the rows equality actually
///    matches. <see cref="GeneratedRepositoryCompositeKeyTests" /> pins the same concern for a composite key; this
///    class is the single-column sibling, covering the shape ADR 0011 admits — a not-null foreign key against a
///    nullable <c>[Unique]</c> target.
/// </summary>
public class GeneratedRepositoryNullableUniqueRelationTargetTests : TestBase
{
   private CatalogEntryRepository _entries = null!;
   private CatalogItemRepository _items = null!;

   private long _widgetEntryId;
   private long _nullSkuEntryId;

   public GeneratedRepositoryNullableUniqueRelationTargetTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _entries = new CatalogEntryRepository(Db);
      _items = new CatalogItemRepository(Db);

      _widgetEntryId = await CreateEntryAsync("widget-1");
      _nullSkuEntryId = await CreateEntryAsync(null); // A target row whose unique column is null: equality can reach it from nowhere.

      await CreateItemAsync("widget-1");
      await CreateItemAsync("gizmo-9"); // Matches no entry: the foreign key points nowhere.
   }

   [Fact]
   public void Query_ReachingTheNullableUniqueRelationTarget_ConstrainsWithPlainEquality()
   {
      var sql = QueryDiagnostics.RenderSql(_items.Query().Where(x => x.Entry!.Sku == "widget-1"));

      sql.Should().MatchRegex(SqlShape.CrossTableEquality("sku", "sku"));
      sql.Should().NotContain("IS NULL", "a widened join condition is what would cost the unique index behind the column");
   }

   [Fact]
   public void Query_ReachingTheNullableUniqueRelationTargetWithoutFilteringIt_RendersAnOuterJoin()
   {
      var sql = QueryDiagnostics.RenderSql(_items.Query().Where(x => x.ItemId > 0).Select(x => x.Entry!.Sku));

      sql.Should().Contain("LEFT JOIN");
      sql.Should().MatchRegex(SqlShape.CrossTableEquality("sku", "sku"));
   }

   [Fact]
   public async Task Query_MaterializingTheRelation_ReachesTheRelatedRowAndLeavesTheUnmatchedRowEmpty()
   {
      var items = await _items.Query()
         .Include(x => x.Entry)
         .OrderBy(x => x.Sku)
         .ToListAsync(CancellationToken);

      items.Single(x => x.Sku == "widget-1").Entry!.EntryId.Should().Be(_widgetEntryId);

      // gizmo-9 matches no entry: the row survives with nothing attached, because a relation is an outer join.
      items.Single(x => x.Sku == "gizmo-9").Entry.Should().BeNull();
   }

   [Fact]
   public async Task Query_MaterializingTheRelation_NeverReachesTheEntryWhoseUniqueColumnIsNull()
   {
      // Equality never matches null, so no item's foreign key can resolve to the entry whose sku is null — whatever
      // that entry's own row looks like, the relation cannot reach it. (An unmatched outer join also carries a null
      // Entry, which is why this asserts against the specific entry id rather than "Entry is null".)
      var items = await _items.Query()
         .Include(x => x.Entry)
         .ToListAsync(CancellationToken);

      items.Should().NotContain(x => x.Entry != null && x.Entry.EntryId == _nullSkuEntryId);
   }

   private async Task<long> CreateEntryAsync(string? sku)
   {
      var entry = await _entries.CreateAsync(new CreateCatalogEntryCommand { Sku = sku }, CancellationToken);

      return entry.EntryId;
   }

   private async Task CreateItemAsync(string sku)
   {
      await _items.CreateAsync(new CreateCatalogItemCommand { Sku = sku }, CancellationToken);
   }
}
