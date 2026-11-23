using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

public class GeneratedRepositoryArrayColumnTests : TestBase
{
   private ArrayColumnRepository _repository = null!;

   public GeneratedRepositoryArrayColumnTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _repository = new ArrayColumnRepository(Db);
   }

   [Fact]
   public async Task Create_WithNonEmptyArrays_RoundTripsThroughGetByKeyAndQuery()
   {
      var created = await CreateAsync(["en", "nl"], null, [1, 2, 3]);

      var byKey = await _repository.GetByPrimaryKeyAsync(created.ArrayColumnId, CancellationToken);
      var byQuery = await _repository.Query().Where(x => x.ArrayColumnId == created.ArrayColumnId).ToListAsync(CancellationToken);

      byKey.Should().NotBeNull();
      byKey!.Locales.Should().Equal("en", "nl");
      byKey.Tags.Should().BeNull();
      byKey.Numbers.Should().Equal(1, 2, 3);
      byQuery.Should().ContainSingle();
      byQuery[0].Locales.Should().Equal("en", "nl");
      byQuery[0].Tags.Should().BeNull();
      byQuery[0].Numbers.Should().Equal(1, 2, 3);
   }

   [Fact]
   public async Task Create_WithEmptyArrays_RoundTripsThemAsEmptyNotNull()
   {
      var created = await CreateAsync([], [], []);

      var byKey = await _repository.GetByPrimaryKeyAsync(created.ArrayColumnId, CancellationToken);
      var byQuery = await _repository.Query().Where(x => x.ArrayColumnId == created.ArrayColumnId).SingleAsync(CancellationToken);

      byKey!.Locales.Should().BeEmpty();
      byKey.Tags.Should().NotBeNull().And.BeEmpty();
      byKey.Numbers.Should().BeEmpty();
      byQuery.Locales.Should().BeEmpty();
      byQuery.Tags.Should().NotBeNull().And.BeEmpty();
      byQuery.Numbers.Should().BeEmpty();
   }

   [Fact]
   public async Task Create_WithANullNullableArray_StoresNullAndReadsNull()
   {
      var created = await CreateAsync(["en"], null, [7]);

      created.Tags.Should().BeNull();
      (await ReadColumnAsync<string?>("tags::TEXT", created.ArrayColumnId)).Should().BeNull();
   }

   [Fact]
   public async Task Create_StoresTheArraysAsPostgresArrays()
   {
      var created = await CreateAsync(["en", "nl"], [], [4, 5]);

      (await ReadColumnAsync<string>("locales::TEXT", created.ArrayColumnId)).Should().Be("{en,nl}");
      (await ReadColumnAsync<string>("tags::TEXT", created.ArrayColumnId)).Should().Be("{}");
      (await ReadColumnAsync<string>("numbers::TEXT", created.ArrayColumnId)).Should().Be("{4,5}");
   }

   [Fact]
   public async Task Update_ReplacesEachArrayIncludingToEmptyAndToNull()
   {
      var created = await CreateAsync(["en"], ["a"], [1]);

      var toEmptyAndNull = await _repository.UpdateAsync(
         new UpdateArrayColumnCommand { ArrayColumnId = created.ArrayColumnId, Locales = [], Tags = null, Numbers = [] },
         CancellationToken
      );

      toEmptyAndNull.Locales.Should().BeEmpty();
      toEmptyAndNull.Tags.Should().BeNull();
      toEmptyAndNull.Numbers.Should().BeEmpty();
      (await ReadColumnAsync<string>("locales::TEXT", created.ArrayColumnId)).Should().Be("{}");
      (await ReadColumnAsync<string?>("tags::TEXT", created.ArrayColumnId)).Should().BeNull();
      (await ReadColumnAsync<string>("numbers::TEXT", created.ArrayColumnId)).Should().Be("{}");

      var toValues = await _repository.UpdateAsync(
         new UpdateArrayColumnCommand { ArrayColumnId = created.ArrayColumnId, Locales = ["en", "nl"], Tags = ["x", "y"], Numbers = [9, 8] },
         CancellationToken
      );

      toValues.Locales.Should().Equal("en", "nl");
      toValues.Tags.Should().Equal("x", "y");
      toValues.Numbers.Should().Equal(9, 8);

      var byQuery = await _repository.Query().Where(x => x.ArrayColumnId == created.ArrayColumnId).SingleAsync(CancellationToken);

      byQuery.Locales.Should().Equal("en", "nl");
      byQuery.Tags.Should().Equal("x", "y");
      byQuery.Numbers.Should().Equal(9, 8);
      (await ReadColumnAsync<string>("locales::TEXT", created.ArrayColumnId)).Should().Be("{en,nl}");
   }

   [Fact]
   public async Task Query_FilteringAnArrayColumnWithContains_ThrowsQueryTranslationException()
   {
      await CreateAsync(["en", "nl"], null, [1]);

      var query = _repository.Query().Where(x => x.Locales.Contains("nl"));

      var asyncFailure = await Record.ExceptionAsync(() => query.ToListAsync(CancellationToken));
      var syncFailure = Record.Exception(() => query.ToList());

      asyncFailure.Should().BeOfType<QueryTranslationException>();
      syncFailure.Should().BeOfType<QueryTranslationException>();
   }

   private async Task<ArrayColumnData> CreateAsync(string[] locales, string[]? tags, int[] numbers)
   {
      return await _repository.CreateAsync(
         new CreateArrayColumnCommand { Locales = locales, Tags = tags, Numbers = numbers },
         CancellationToken
      );
   }

   private async Task<T> ReadColumnAsync<T>(string columnExpression, long arrayColumnId)
   {
      return await Db.Dapper.QuerySingleAsync<T>(
         $"SELECT {columnExpression} FROM public.generated_array_columns WHERE array_column_id = :arrayColumnId",
         new Dictionary<string, object?> { ["arrayColumnId"] = arrayColumnId },
         ct: CancellationToken
      );
   }
}
