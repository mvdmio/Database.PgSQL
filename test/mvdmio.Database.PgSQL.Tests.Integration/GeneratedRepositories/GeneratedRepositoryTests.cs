using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

using global::Mvdmio.Database.PgSQL.Tests.Integration;

public class GeneratedRepositoryTests : TestBase
{
   private UserRepository _repository = null!;

   public GeneratedRepositoryTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      await Db.Dapper.ExecuteAsync(
         """
         CREATE TABLE IF NOT EXISTS public.generated_users (
            user_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            user_name TEXT NOT NULL UNIQUE,
            first_name TEXT NOT NULL
         )
         """,
         ct: CancellationToken
      );

      _repository = new UserRepository(Db);
   }

   [Fact]
   public async Task CrudOperations_WorkEndToEnd()
   {
      var created = await _repository.CreateAsync(new CreateUserCommand
      {
         UserName = "alice",
         FirstName = "Alice"
      }, CancellationToken);

      created.UserId.Should().BeGreaterThan(0);
      created.UserName.Should().Be("alice");
      created.FirstName.Should().Be("Alice");

      var allUsers = (await _repository.GetAllAsync(CancellationToken)).ToList();
      allUsers.Should().ContainSingle();

      var byId = await _repository.GetByUserIdAsync(created.UserId, CancellationToken);
      byId.Should().NotBeNull();
      byId!.UserName.Should().Be("alice");

      var byUnique = await _repository.GetByUserNameAsync("alice", CancellationToken);
      byUnique.Should().NotBeNull();
      byUnique!.UserId.Should().Be(created.UserId);

      var updated = await _repository.UpdateAsync(new UpdateUserCommand
      {
         UserId = created.UserId,
         UserName = "alice-updated",
         FirstName = "Alicia"
      }, CancellationToken);

      updated.UserName.Should().Be("alice-updated");
      updated.FirstName.Should().Be("Alicia");

      var deletedByUnique = await _repository.DeleteByUserNameAsync("alice-updated", CancellationToken);
      deletedByUnique.Should().BeTrue();

      var missingAfterDelete = await _repository.GetByUserIdAsync(created.UserId, CancellationToken);
      missingAfterDelete.Should().BeNull();
   }

   [Fact]
   public async Task DeleteById_WhenMissing_ReturnsFalse()
   {
      var deleted = await _repository.DeleteByUserIdAsync(999999, CancellationToken);

      deleted.Should().BeFalse();
   }

   [Fact]
   public void AddAssemblySpecificRegistration_RegistersGeneratedRepository()
   {
      var services = new ServiceCollection();

      services.AddMvdmioDatabasePgSQLTestsIntegration();

      services.Should().Contain(x => x.ServiceType == typeof(IUserRepository) && x.ImplementationType == typeof(UserRepository) && x.Lifetime == ServiceLifetime.Scoped);
   }
}
