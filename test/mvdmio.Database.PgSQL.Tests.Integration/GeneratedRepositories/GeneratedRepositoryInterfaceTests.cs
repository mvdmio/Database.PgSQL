using AwesomeAssertions;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    Query() is declared on the generated interface, so a caller can be handed a fake instead of a database.
/// </summary>
public class GeneratedRepositoryInterfaceTests
{
   [Fact]
   public void Query_OnAFakeRepository_ReturnsTheFakeRows()
   {
      IProfileRepository repository = new FakeProfileRepository([
         new ProfileData { Handle = "alice" },
         new ProfileData { Handle = "bob" }
      ]);

      var handles = repository.Query().Where(x => x.Handle == "bob").Select(x => x.Handle).ToList();

      handles.Should().Equal("bob");
   }

   private sealed class FakeProfileRepository : IProfileRepository
   {
      private readonly List<ProfileData> _profiles;

      public FakeProfileRepository(List<ProfileData> profiles)
      {
         _profiles = profiles;
      }

      public IQueryable<ProfileData> Query(TimeSpan? commandTimeout = null)
      {
         return _profiles.AsQueryable();
      }

      public Task<ProfileData> CreateAsync(CreateProfileCommand data, CancellationToken ct = default) => throw new NotSupportedException();
      public Task<IEnumerable<ProfileData>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
      public Task<ProfileData?> GetByProfileIdAsync(long profileId, CancellationToken ct = default) => throw new NotSupportedException();
      public Task<ProfileData?> GetByHandleAsync(string handle, CancellationToken ct = default) => throw new NotSupportedException();
      public Task<ProfileData> UpdateAsync(UpdateProfileCommand data, CancellationToken ct = default) => throw new NotSupportedException();
      public Task<bool> DeleteByProfileIdAsync(long profileId, CancellationToken ct = default) => throw new NotSupportedException();
      public Task<bool> DeleteByHandleAsync(string handle, CancellationToken ct = default) => throw new NotSupportedException();
   }
}
