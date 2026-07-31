using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    What a declared tenancy column buys on <c>Query</c> and <c>GetAllAsync</c>, over a real PostgreSQL container —
///    the two members this step of the spec touches. Covered on both shapes ADR 0009 distinguishes: the tenancy column
///    as part of the primary key (<see cref="TenancyDocumentTable" />) and outside a surrogate key
///    (<see cref="TenancySettingTable" />).
/// </summary>
public class GeneratedRepositoryTenancyTests : TestBase
{
   private const long FIRST_ACCOUNT = 1;
   private const long SECOND_ACCOUNT = 2;

   private TenancyDocumentRepository _documents = null!;
   private TenancySettingRepository _settings = null!;

   public GeneratedRepositoryTenancyTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _documents = new TenancyDocumentRepository(Db);
      _settings = new TenancySettingRepository(Db);

      await CreateDocumentAsync(FIRST_ACCOUNT, "doc-first-a", "First A");
      await CreateDocumentAsync(FIRST_ACCOUNT, "doc-first-b", "First B");
      await CreateDocumentAsync(SECOND_ACCOUNT, "doc-second-a", "Second A");

      await CreateSettingAsync(FIRST_ACCOUNT, "setting-first-a", "First A");
      await CreateSettingAsync(FIRST_ACCOUNT, "setting-first-b", "First B");
      await CreateSettingAsync(SECOND_ACCOUNT, "setting-second-a", "Second A");
   }

   [Fact]
   public async Task Query_OnATableWhoseTenancyColumnIsPartOfTheKey_ReturnsOnlyTheCallersRows()
   {
      var rows = await _documents.Query(FIRST_ACCOUNT).ToListAsync(CancellationToken);

      rows.Select(x => x.Code).Should().BeEquivalentTo("doc-first-a", "doc-first-b");
   }

   [Fact]
   public async Task GetAllAsync_OnATableWhoseTenancyColumnIsPartOfTheKey_ReturnsOnlyTheCallersRows()
   {
      var rows = await _documents.GetAllAsync(FIRST_ACCOUNT, CancellationToken);

      rows.Select(x => x.Code).Should().BeEquivalentTo("doc-first-a", "doc-first-b");
   }

   [Fact]
   public async Task Query_OnATableWhoseTenancyColumnIsOutsideTheKey_ReturnsOnlyTheCallersRows()
   {
      var rows = await _settings.Query(SECOND_ACCOUNT).ToListAsync(CancellationToken);

      rows.Select(x => x.Code).Should().BeEquivalentTo("setting-second-a");
   }

   [Fact]
   public async Task GetAllAsync_OnATableWhoseTenancyColumnIsOutsideTheKey_ReturnsOnlyTheCallersRows()
   {
      var rows = await _settings.GetAllAsync(SECOND_ACCOUNT, CancellationToken);

      rows.Select(x => x.Code).Should().BeEquivalentTo("setting-second-a");
   }

   [Fact]
   public async Task Query_ComposesFurtherFilteringOnTopOfTheTenantPredicateItCannotRemove()
   {
      var rows = await _documents.Query(FIRST_ACCOUNT).Where(x => x.Title == "First B").ToListAsync(CancellationToken);

      rows.Select(x => x.Code).Should().Equal("doc-first-b");
   }

   private async Task CreateDocumentAsync(long accountId, string code, string title)
   {
      await _documents.CreateAsync(
         new CreateTenancyDocumentCommand { AccountId = accountId, Code = code, Title = title, Body = "body" },
         CancellationToken
      );
   }

   private async Task CreateSettingAsync(long accountId, string code, string label)
   {
      await _settings.CreateAsync(
         new CreateTenancySettingCommand { AccountId = accountId, Code = code, Label = label, Value = "value" },
         CancellationToken
      );
   }
}
