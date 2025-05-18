namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

public abstract class TestBase : IAsyncLifetime
{
   private readonly TestFixture _fixture;

   public TestBase(TestFixture fixture)
   {
      _fixture = fixture;
   }
   
   public virtual async ValueTask InitializeAsync()
   {
      await _fixture.Db.BeginTransactionAsync();
   }

   public virtual async ValueTask DisposeAsync()
   {
      await _fixture.Db.RollbackTransactionAsync();
   }
}