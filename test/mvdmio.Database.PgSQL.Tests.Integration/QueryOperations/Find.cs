using FluentAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture.Entities;

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
      var result = _dbContext.SimpleTable.Find(1);
         
      // Assert
      result.Should().BeNull();
   }
   
   [Fact]
   public async Task ShouldReturnRecord_WhenRecordWithIdExists()
   {
      // Arrange
      const int id = 1;
      var record = new SimpleRecord(id, "Test");
      _dbContext.SimpleTable.Insert(record);
      
      // Act
      var result = _dbContext.SimpleTable.Find(id);
         
      // Assert
      result.Should().NotBeNull();
      await Verify(result, VerifySettings);
   }
}