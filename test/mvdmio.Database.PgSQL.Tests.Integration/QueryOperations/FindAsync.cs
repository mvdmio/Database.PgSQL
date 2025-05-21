using FluentAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture.Entities;

namespace mvdmio.Database.PgSQL.Tests.Integration.QueryOperations;

public class DbTableFindAsyncTests : TestBase
{
   private TestDbContext _dbContext = null!;
   
   public DbTableFindAsyncTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      
      _dbContext = new TestDbContext(Db);
   }

   [Fact]
   public async Task ShouldReturnNull_WhenRecordWithIdDoesNotExists()
   {
      // Arrange
      
      // Act
      var result = await _dbContext.SimpleTable.FindAsync(1);
         
      // Assert
      result.Should().BeNull();
   }
   
   [Fact]
   public async Task ShouldReturnRecord_WhenRecordWithIdExists()
   {
      // Arrange
      const int id = 1;
      var record = new SimpleRecord(id, "Test");
      await _dbContext.SimpleTable.InsertAsync(record);
      
      // Act
      var result = await _dbContext.SimpleTable.FindAsync(id);
      
      // Assert
      result.Should().NotBeNull();
      await Verify(result, VerifySettings);
   }
}