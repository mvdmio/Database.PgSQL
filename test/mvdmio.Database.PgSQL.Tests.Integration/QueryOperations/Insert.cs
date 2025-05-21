using FluentAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture.Entities;

namespace mvdmio.Database.PgSQL.Tests.Integration.QueryOperations;

public class DbTableInsertTests : TestBase
{
   private TestDbContext _dbContext = null!;
   
   public DbTableInsertTests(TestFixture fixture)
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
      var result = _dbContext.SimpleTable.Insert(record);
         
      // Assert
      result.Should().NotBeNull();
      await Verify(result, VerifySettings)
         .UseMethodName("ShouldInsertRecordAndReturnWithGeneratedValues_Inserted");
      
      // Verify the record was actually inserted
      var dbRecord = _dbContext.SimpleTable.Find(id);
      dbRecord.Should().NotBeNull();
      await Verify(dbRecord, VerifySettings)
         .UseMethodName("ShouldInsertRecordAndReturnWithGeneratedValues_Retrieved");
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
      var result = _dbContext.SimpleTable.Insert(record);
         
      // Assert
      result.Should().NotBeNull();
      await Verify(result, VerifySettings)
         .UseMethodName("ShouldInsertRecordWithOptionalValues_Inserted");
      
      // Verify the record was actually inserted
      var dbRecord = _dbContext.SimpleTable.Find(id);
      dbRecord.Should().NotBeNull();
      await Verify(dbRecord, VerifySettings)
         .UseMethodName("ShouldInsertRecordWithOptionalValues_Retrieved");
   }
   
   [Fact]
   public void ShouldThrowException_WhenRecordWithSameIdAlreadyExists()
   {
      // Arrange
      const long id = 1;
      var record1 = new SimpleRecord(id, "Test Value 1");
      var record2 = new SimpleRecord(id, "Test Value 2");
      
      _dbContext.SimpleTable.Insert(record1);
      
      // Act
      var act = () => _dbContext.SimpleTable.Insert(record2);
         
      // Assert
      act.Should().Throw<Exception>();
   }
}