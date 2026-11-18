namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

public abstract class SampleConformanceTestBase : ODataTestBase
{
   protected static readonly Guid alicePublicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

   private static readonly CreateSampleCommand[] _rows = [
      new() {
         Name = "alice",
         Nickname = null,
         Rating = 3,
         Bonus = null,
         Amount = 10.50m,
         CreatedAt = new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.Zero),
         IsActive = true,
         Category = SampleCategory.Premium,
         PublicId = alicePublicId,
         Tier = "gold"
      },
      new() {
         Name = "bob",
         Nickname = "bobby",
         Rating = 5,
         Bonus = 2,
         Amount = 20.25m,
         CreatedAt = new DateTimeOffset(2023, 11, 30, 22, 15, 45, TimeSpan.Zero),
         IsActive = false,
         Category = SampleCategory.Standard,
         PublicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
         Tier = "silver"
      },
      new() {
         Name = "carol",
         Nickname = "caz",
         Rating = 8,
         Bonus = 7,
         Amount = 30.00m,
         CreatedAt = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero),
         IsActive = true,
         Category = SampleCategory.Legacy,
         PublicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
         Tier = "gold"
      },
      new() {
         Name = "dave",
         Nickname = null,
         Rating = 5,
         Bonus = 2,
         Amount = 5.75m,
         CreatedAt = new DateTimeOffset(2025, 6, 15, 12, 30, 0, TimeSpan.Zero),
         IsActive = false,
         Category = SampleCategory.Standard,
         PublicId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
         Tier = "silver"
      }
   ];

   protected SampleRepository Repository { get; private set; } = null!;

   protected SampleConformanceTestBase(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      Repository = new SampleRepository(Db);

      foreach (var row in _rows)
      {
         await Repository.CreateAsync(row, CancellationToken);
      }
   }

   protected AppliedQuery Apply(string queryString)
   {
      return ODataQuery.Apply(Repository.Query(), queryString);
   }

   protected static async Task<IReadOnlyList<string>> NamesAsync(AppliedQuery applied)
   {
      var rows = await applied.RowsAsync<SampleData>(CancellationToken);

      return rows.Select(x => x.Name).ToList();
   }
}
