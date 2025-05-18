namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

public class TestDbContext
{
   private readonly DatabaseConnection _db;

   public TestDbContext(DatabaseConnection db)
   {
      _db = db;

      TestTable = new TestTable(db);
   }
   
   public TestTable TestTable { get; }
}