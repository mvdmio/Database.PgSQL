namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

public abstract class TestBase : IAsyncLifetime
{
   private readonly TestFixture _fixture;
   private readonly DatabaseConnectionFactory _databaseConnectionFactory;

   protected VerifySettings VerifySettings { get; }
   protected DatabaseConnection Db { get; private set; } = null!;

   protected TestBase(TestFixture fixture)
   {
      _fixture = fixture;
      _databaseConnectionFactory = new DatabaseConnectionFactory();

      VerifySettings = new VerifySettings();
      VerifySettings.UseDirectory(".verify");
   }
   
   public virtual async ValueTask InitializeAsync()
   {
      Db = _databaseConnectionFactory.ForConnectionString(_fixture.DbContainer.GetConnectionString());
      
      await Db.BeginTransactionAsync();
   }

   public virtual async ValueTask DisposeAsync()
   {
      await Db.RollbackTransactionAsync();
   }
}