namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

public abstract class ODataTestBase : IAsyncLifetime
{
   private readonly ODataTestFixture _fixture;
   private readonly DatabaseConnectionFactory _databaseConnectionFactory;

   protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
   protected DatabaseConnection Db { get; private set; } = null!;

   protected ODataTestBase(ODataTestFixture fixture)
   {
      _fixture = fixture;
      _databaseConnectionFactory = new DatabaseConnectionFactory();
   }

   public virtual async ValueTask InitializeAsync()
   {
      Db = _databaseConnectionFactory.BuildConnection(_fixture.DbContainer.GetConnectionString());

      await Db.BeginTransactionAsync();
   }

   public virtual async ValueTask DisposeAsync()
   {
      await Db.RollbackTransactionAsync();
      await _databaseConnectionFactory.DisposeAsync();
   }
}
