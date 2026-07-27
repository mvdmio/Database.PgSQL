namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    Opens a connection through the factory and a transaction before each test, and rolls the transaction back
///    afterwards, so every test leaves the database as it found it.
/// </summary>
/// <remarks>
///    A deliberate copy of the main integration suite's base class rather than a project reference to it, the same
///    choice the secondary-schema project makes, so the two test assemblies stay independent. There is no HTTP
///    boundary here, so the rollback pattern works exactly as it does there.
/// </remarks>
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
