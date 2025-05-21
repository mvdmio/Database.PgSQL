using FluentAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture.Entities;

namespace mvdmio.Database.PgSQL.Tests.Integration.QueryOperations;

public class DbTableInsertAsyncTests : TestBase
{
   private TestDbContext _dbContext = null!;
   
   public DbTableInsertAsyncTests(TestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      
      _dbContext = new TestDbContext(Db);
   }

   [Fact]
   public async Task ShouldInsertRecordAndReturnWithGeneratedValues()
   {
      // Arrange
      const long id = 1;
      var record = new SimpleRecord(id, "Test Value");
      
      // Act
      var result = await _dbContext.SimpleTable.InsertAsync(record);
         
      // Assert
      result.Should().NotBeNull();
      await Verify(result, VerifySettings).UseMethodName("ShouldInsertRecordAndReturnWithGeneratedValues_Inserted");
      
      // Verify the record was actually inserted
      var dbRecord = await _dbContext.SimpleTable.FindAsync(id);
      dbRecord.Should().NotBeNull();
      await Verify(dbRecord, VerifySettings).UseMethodName("ShouldInsertRecordAndReturnWithGeneratedValues_Retrieved");
   }

   [Fact]
   public async Task ShouldInsertRecordWithOptionalValues()
   {
      // Arrange
      const long id = 1;
      var record = new SimpleRecord(id, "Test Value")
      {
         OptionalStringValue = "Optional Value"
      };
      
      // Act
      var result = await _dbContext.SimpleTable.InsertAsync(record);
         
      // Assert
      result.Should().NotBeNull();
      await Verify(result, VerifySettings).UseMethodName("ShouldInsertRecordWithOptionalValues_Inserted");
      
      // Verify the record was actually inserted
      var dbRecord = await _dbContext.SimpleTable.FindAsync(id);
      dbRecord.Should().NotBeNull();
      await Verify(dbRecord, VerifySettings).UseMethodName("ShouldInsertRecordWithOptionalValues_Retrieved");
   }

   [Fact]
   public async Task ShouldThrowException_WhenRecordWithSameIdAlreadyExists()
   {
      // Arrange
      const long id = 1;
      var record1 = new SimpleRecord(id, "Test Value 1");
      var record2 = new SimpleRecord(id, "Test Value 2");
      
      await _dbContext.SimpleTable.InsertAsync(record1);
      
      // Act
      var act = () => _dbContext.SimpleTable.InsertAsync(record2);
         
      // Assert
      await act.Should().ThrowAsync<Exception>();
   }
}