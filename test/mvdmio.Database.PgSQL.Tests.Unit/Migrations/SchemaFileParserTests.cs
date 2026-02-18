using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

public class SchemaFileParserTests
{
   private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;
   [Fact]
   public void ParseMigrationVersion_WithValidHeader_ReturnsMigrationInfo()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Generated at 2026-02-18 10:30:45 UTC
         -- Migration version: 202602161430 (AddUsersTable)
         --

         CREATE TABLE users (id SERIAL PRIMARY KEY);
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().NotBeNull();
      result!.Value.Identifier.Should().Be(202602161430);
      result.Value.Name.Should().Be("AddUsersTable");
   }

   [Fact]
   public void ParseMigrationVersion_WithNoMigrationVersion_ReturnsNull()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Generated at 2026-02-18 10:30:45 UTC
         -- Migration version: (none)
         --

         CREATE TABLE users (id SERIAL PRIMARY KEY);
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().BeNull();
   }

   [Fact]
   public void ParseMigrationVersion_WithEmptyContent_ReturnsNull()
   {
      var result = SchemaFileParser.ParseMigrationVersion("");

      result.Should().BeNull();
   }

   [Fact]
   public void ParseMigrationVersion_WithNoHeader_ReturnsNull()
   {
      var content = "CREATE TABLE users (id SERIAL PRIMARY KEY);";

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().BeNull();
   }

   [Fact]
   public void ParseMigrationVersion_WithMalformedIdentifier_ReturnsNull()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Migration version: not-a-number (AddUsersTable)
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().BeNull();
   }

   [Fact]
   public void ParseMigrationVersion_WithComplexMigrationName_ReturnsMigrationInfo()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Migration version: 202602181500 (Add_Users_And_Orders_Tables_v2)
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().NotBeNull();
      result!.Value.Identifier.Should().Be(202602181500);
      result.Value.Name.Should().Be("Add_Users_And_Orders_Tables_v2");
   }

   [Fact]
   public void ParseMigrationVersion_WithSpacesInName_ReturnsMigrationInfo()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Migration version: 202602181500 (Add Users Table)
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().NotBeNull();
      result!.Value.Identifier.Should().Be(202602181500);
      result.Value.Name.Should().Be("Add Users Table");
   }

   [Fact]
   public void ParseMigrationVersion_WithExtraWhitespace_ReturnsMigrationInfo()
   {
      var content = """
         --
         -- PostgreSQL database schema
         --   Migration version:   202602181500   (  AddUsersTable  )   
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().NotBeNull();
      result!.Value.Identifier.Should().Be(202602181500);
      result.Value.Name.Should().Be("AddUsersTable");
   }

   [Fact]
   public void ParseMigrationVersion_WithLargeIdentifier_ReturnsMigrationInfo()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Migration version: 999912312359 (FinalMigration)
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().NotBeNull();
      result!.Value.Identifier.Should().Be(999912312359);
      result.Value.Name.Should().Be("FinalMigration");
   }

   [Fact]
   public void ParseMigrationVersion_MigrationVersionLineInMiddleOfFile_ReturnsMigrationInfo()
   {
      var content = """
         -- Some other comment
         -- Generated at 2026-02-18 10:30:45 UTC
         -- Migration version: 202602161430 (TestMigration)
         -- More comments
         CREATE TABLE test (id INT);
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().NotBeNull();
      result!.Value.Identifier.Should().Be(202602161430);
      result.Value.Name.Should().Be("TestMigration");
   }

   [Fact]
   public async Task ParseMigrationVersionFromFileAsync_WithValidFile_ReturnsMigrationInfo()
   {
      var tempFile = Path.GetTempFileName();

      try
      {
         await File.WriteAllTextAsync(tempFile, """
            --
            -- PostgreSQL database schema
            -- Migration version: 202602161430 (AddUsersTable)
            --
            """, CancellationToken);

         var result = await SchemaFileParser.ParseMigrationVersionFromFileAsync(tempFile, CancellationToken);

         result.Should().NotBeNull();
         result!.Value.Identifier.Should().Be(202602161430);
         result.Value.Name.Should().Be("AddUsersTable");
      }
      finally
      {
         File.Delete(tempFile);
      }
   }

   [Fact]
   public async Task ParseMigrationVersionFromFileAsync_WithMissingFile_ThrowsFileNotFoundException()
   {
      var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".sql");

      var action = async () => await SchemaFileParser.ParseMigrationVersionFromFileAsync(nonExistentPath, CancellationToken);

      await action.Should().ThrowAsync<FileNotFoundException>();
   }

   [Fact]
   public void ParseMigrationVersionFromFile_WithValidFile_ReturnsMigrationInfo()
   {
      var tempFile = Path.GetTempFileName();

      try
      {
         File.WriteAllText(tempFile, """
            --
            -- PostgreSQL database schema
            -- Migration version: 202602161430 (AddUsersTable)
            --
            """);

         var result = SchemaFileParser.ParseMigrationVersionFromFile(tempFile);

         result.Should().NotBeNull();
         result!.Value.Identifier.Should().Be(202602161430);
         result.Value.Name.Should().Be("AddUsersTable");
      }
      finally
      {
         File.Delete(tempFile);
      }
   }

   [Fact]
   public void ParseMigrationVersionFromFile_WithMissingFile_ThrowsFileNotFoundException()
   {
      var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".sql");

      var action = () => SchemaFileParser.ParseMigrationVersionFromFile(nonExistentPath);

      action.Should().Throw<FileNotFoundException>();
   }
}
