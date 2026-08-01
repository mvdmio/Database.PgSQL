using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    What a declared tenancy column buys over a real PostgreSQL container: which rows the generated reads return,
///    which rows the generated deletes leave in place, and what the generated writes put in the column. Covered on
///    both shapes ADR 0009 distinguishes — the tenancy column as part of the primary key
///    (<see cref="TenancyDocumentTable" />) and outside a surrogate key (<see cref="TenancySettingTable" />).
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

   [Fact]
   public async Task GetByCodeAsync_ForAValueBelongingToAnotherTenant_ReturnsNull_TenancyOutsideTheKey()
   {
      var row = await _settings.GetByCodeAsync(FIRST_ACCOUNT, "setting-second-a", CancellationToken);

      row.Should().BeNull();
   }

   [Fact]
   public async Task DeleteByCodeAsync_ForAValueBelongingToAnotherTenant_LeavesTheRowInPlace_TenancyOutsideTheKey()
   {
      var deleted = await _settings.DeleteByCodeAsync(FIRST_ACCOUNT, "setting-second-a", CancellationToken);
      var stillThere = await _settings.GetByCodeAsync(SECOND_ACCOUNT, "setting-second-a", CancellationToken);

      deleted.Should().BeFalse();
      stillThere.Should().NotBeNull();
   }

   [Fact]
   public async Task GetByCodeAsync_ForAValueBelongingToAnotherTenant_ReturnsNull_TenancyInsideTheKey()
   {
      var row = await _documents.GetByCodeAsync(SECOND_ACCOUNT, "doc-first-a", CancellationToken);

      row.Should().BeNull();
   }

   [Fact]
   public async Task DeleteByCodeAsync_ForAValueBelongingToAnotherTenant_LeavesTheRowInPlace_TenancyInsideTheKey()
   {
      var deleted = await _documents.DeleteByCodeAsync(SECOND_ACCOUNT, "doc-first-a", CancellationToken);
      var stillThere = await _documents.GetByCodeAsync(FIRST_ACCOUNT, "doc-first-a", CancellationToken);

      deleted.Should().BeFalse();
      stillThere.Should().NotBeNull();
   }

   [Fact]
   public async Task GetByPrimaryKeyAsync_WithTheWrongTenant_ReturnsNull_WhenTheTenancyColumnIsOutsideTheKey()
   {
      var mine = await _settings.GetByCodeAsync(SECOND_ACCOUNT, "setting-second-a", CancellationToken);
      mine.Should().NotBeNull();

      var row = await _settings.GetByPrimaryKeyAsync(FIRST_ACCOUNT, mine!.SettingId, CancellationToken);

      row.Should().BeNull();
   }

   [Fact]
   public async Task GetByPrimaryKeyAsync_SignatureIsUnchanged_WhenTheTenancyColumnIsAlreadyAKeyMember()
   {
      var mine = await _documents.GetByCodeAsync(FIRST_ACCOUNT, "doc-first-a", CancellationToken);
      mine.Should().NotBeNull();

      // The signature takes only the key — the tenant is already one of its members — so the wrong account cannot
      // even be asked for separately from the key itself.
      var row = await _documents.GetByPrimaryKeyAsync(FIRST_ACCOUNT, mine!.DocumentId, CancellationToken);

      row.Should().NotBeNull();
   }

   [Fact]
   public async Task CreateAsync_WritesTheRowUnderTheTenantTheRequiredPropertyCarries()
   {
      var created = await _documents.CreateAsync(
         new CreateTenancyDocumentCommand { AccountId = SECOND_ACCOUNT, Code = "doc-created", Title = "Created", Body = "body" },
         CancellationToken
      );

      created.AccountId.Should().Be(SECOND_ACCOUNT);

      var mine = await _documents.GetByCodeAsync(SECOND_ACCOUNT, "doc-created", CancellationToken);
      mine.Should().NotBeNull();

      var notSomeoneElses = await _documents.GetByCodeAsync(FIRST_ACCOUNT, "doc-created", CancellationToken);
      notSomeoneElses.Should().BeNull();
   }

   [Fact]
   public async Task UpdateAsync_AimedAtAnotherTenantsRow_ChangesNothingAndThrows_TenancyInsideTheKey()
   {
      var mine = await _documents.GetByCodeAsync(FIRST_ACCOUNT, "doc-first-a", CancellationToken);
      mine.Should().NotBeNull();

      Func<Task> action = () => _documents.UpdateAsync(
         new UpdateTenancyDocumentCommand { AccountId = SECOND_ACCOUNT, DocumentId = mine!.DocumentId, Code = mine.Code, Title = "Hijacked", Body = mine.Body },
         CancellationToken
      );

      // Wrapped the same way any update against a missing key already throws — a mismatched WHERE, not a special case.
      await action.Should().ThrowAsync<QueryException>();

      var stillMine = await _documents.GetByCodeAsync(FIRST_ACCOUNT, "doc-first-a", CancellationToken);
      stillMine!.Title.Should().Be("First A");
   }

   [Fact]
   public async Task UpdateAsync_AimedAtAnotherTenantsRow_ChangesNothingAndThrows_TenancyOutsideTheKey()
   {
      var mine = await _settings.GetByCodeAsync(SECOND_ACCOUNT, "setting-second-a", CancellationToken);
      mine.Should().NotBeNull();

      Func<Task> action = () => _settings.UpdateAsync(
         new UpdateTenancySettingCommand { SettingId = mine!.SettingId, AccountId = FIRST_ACCOUNT, Code = mine.Code, Label = mine.Label, Value = "Hijacked" },
         CancellationToken
      );

      // Wrapped the same way any update against a missing key already throws — a mismatched WHERE, not a special case.
      await action.Should().ThrowAsync<QueryException>();

      var stillMine = await _settings.GetByCodeAsync(SECOND_ACCOUNT, "setting-second-a", CancellationToken);
      stillMine!.Value.Should().Be("value");
   }

   [Fact]
   public async Task UpdateAsync_OfTheCallersOwnRow_LeavesTheTenancyColumnAsItWas()
   {
      var mine = await _settings.GetByCodeAsync(SECOND_ACCOUNT, "setting-second-a", CancellationToken);
      mine.Should().NotBeNull();

      var updated = await _settings.UpdateAsync(
         new UpdateTenancySettingCommand { SettingId = mine!.SettingId, AccountId = SECOND_ACCOUNT, Code = mine.Code, Label = mine.Label, Value = "updated" },
         CancellationToken
      );

      updated.AccountId.Should().Be(SECOND_ACCOUNT);
      updated.Value.Should().Be("updated");
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
