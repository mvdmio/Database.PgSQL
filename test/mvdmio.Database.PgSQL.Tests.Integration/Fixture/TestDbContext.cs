using mvdmio.Database.PgSQL.Tests.Integration.Fixture.Entities;

namespace mvdmio.Database.PgSQL.Tests.Integration.Fixture;

public class TestDbContext
{
   public SimpleRecordDbTable SimpleTable { get; }
   public ComplexRecordDbTable ComplexTable { get; }
   
   public TestDbContext(DatabaseConnection db)
   {
      SimpleTable = new SimpleRecordDbTable(db);
      ComplexTable = new ComplexRecordDbTable(db);
   }
}