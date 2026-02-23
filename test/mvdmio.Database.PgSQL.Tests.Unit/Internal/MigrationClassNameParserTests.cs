using AwesomeAssertions;
using mvdmio.Database.PgSQL.Internal;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace mvdmio.Database.PgSQL.Tests.Unit.Internal;

public class MigrationClassNameParserTests
{
   // -----------------------------------------------------------------------
   // ParseIdentifier
   // -----------------------------------------------------------------------

   [Fact]
   public void ParseIdentifier_WithValidName_ReturnsIdentifier()
   {
      var result = MigrationClassNameParser.ParseIdentifier("_202310191050_AddUsersTable");

      result.Should().Be(202310191050L);
   }

   [Fact]
   public void ParseIdentifier_WithoutLeadingUnderscore_ReturnsIdentifier()
   {
      var result = MigrationClassNameParser.ParseIdentifier("202310191050_AddUsersTable");

      result.Should().Be(202310191050L);
   }

   [Fact]
   public void ParseIdentifier_WithInvalidName_ThrowsFormatException()
   {
      var act = () => MigrationClassNameParser.ParseIdentifier("AddUsersTableMigration");

      act.Should().Throw<FormatException>();
   }

   [Fact]
   public void ParseIdentifier_WithIdentifierTooShort_ThrowsFormatException()
   {
      // 10 digits, not 12
      var act = () => MigrationClassNameParser.ParseIdentifier("_2023101910_AddUsers");

      act.Should().Throw<FormatException>();
   }

   [Fact]
   public void ParseIdentifier_WithMissingNamePart_ThrowsFormatException()
   {
      var act = () => MigrationClassNameParser.ParseIdentifier("_202310191050_");

      act.Should().Throw<FormatException>();
   }

   // -----------------------------------------------------------------------
   // ParseName
   // -----------------------------------------------------------------------

   [Fact]
   public void ParseName_WithValidName_ReturnsName()
   {
      var result = MigrationClassNameParser.ParseName("_202310191050_AddUsersTable");

      result.Should().Be("AddUsersTable");
   }

   [Fact]
   public void ParseName_WithoutLeadingUnderscore_ReturnsName()
   {
      var result = MigrationClassNameParser.ParseName("202310191050_AddUsersTable");

      result.Should().Be("AddUsersTable");
   }

   [Fact]
   public void ParseName_WithNameContainingUnderscores_ReturnsFullName()
   {
      var result = MigrationClassNameParser.ParseName("_202310191050_Add_Users_Table");

      result.Should().Be("Add_Users_Table");
   }

   [Fact]
   public void ParseName_WithInvalidName_ThrowsFormatException()
   {
      var act = () => MigrationClassNameParser.ParseName("AddUsersTableMigration");

      act.Should().Throw<FormatException>();
   }

   // -----------------------------------------------------------------------
   // IsValidClassName
   // -----------------------------------------------------------------------

   [Theory]
   [InlineData("_202310191050_AddUsersTable", true)]
   [InlineData("202310191050_AddUsersTable", true)]
   [InlineData("_202310191050_Add_Users_Table", true)]
   [InlineData("AddUsersTableMigration", false)]
   [InlineData("_2023101910_AddUsers", false)]   // 10 digits
   [InlineData("_202310191050_", false)]          // empty name
   [InlineData("", false)]
   public void IsValidClassName_ReturnsExpectedResult(string className, bool expectedResult)
   {
      var result = MigrationClassNameParser.IsValidClassName(className);

      result.Should().Be(expectedResult);
   }

   // -----------------------------------------------------------------------
   // IDbMigration default property integration
   // -----------------------------------------------------------------------

   [Fact]
   public void IDbMigration_DefaultIdentifier_ParsedFromClassName()
   {
      IDbMigration migration = new _202310191050_TestMigration();

      migration.Identifier.Should().Be(202310191050L);
   }

   [Fact]
   public void IDbMigration_DefaultName_ParsedFromClassName()
   {
      IDbMigration migration = new _202310191050_TestMigration();

      migration.Name.Should().Be("TestMigration");
   }

   // Minimal concrete migration used only for the integration tests above
   private sealed class _202310191050_TestMigration : IDbMigration
   {
      public Task UpAsync(DatabaseConnection db) => Task.CompletedTask;
   }
}
