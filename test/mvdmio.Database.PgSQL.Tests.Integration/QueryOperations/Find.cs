using FluentAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.QueryOperations;

public class DbTableFindTests : TestBase
{
   private TestDbContext _dbContext = null!;
   
   public DbTableFindTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      
      _dbContext = new TestDbContext(Db);
   }

   [Fact]
   public void ShouldReturnNull_WhenRecordWithIdDoesNotExists()
   {
      // Arrange
      
      // Act
      var result = _dbContext.TestTable.Find(1);
         
      // Assert
      result.Should().BeNull();
   }
}