using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
using mvdmio.Database.PgSQL.Migrations.Models;

namespace mvdmio.Database.PgSQL.Tests.Unit.Migrations;

public class SchemaFileParserTests
{
   private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   [Fact]
   public void ParseMigrationVersion_WithLegacyScopelessHeader_ReturnsSingleEntryWithNullScope()
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

      result.Should().ContainSingle();
      result[0].Identifier.Should().Be(202602161430);
      result[0].Name.Should().Be("AddUsersTable");
      result[0].Scope.Should().BeNull();
   }

   [Fact]
   public void ParseMigrationVersion_WithSinglePerScopeLine_ReturnsEntryWithScope()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Migration version: 202602161430 (AddUsersTable) [MyApp.Data]
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().ContainSingle();
      result[0].Identifier.Should().Be(202602161430);
      result[0].Name.Should().Be("AddUsersTable");
      result[0].Scope.Should().Be("MyApp.Data");
   }

   [Fact]
   public void ParseMigrationVersion_WithMultiplePerScopeLines_ReturnsAllEntriesInFileOrder()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Migration version: 202602161430 (AddUsersTable) [MyApp.Data]
         -- Migration version: 202601010900 (AddInvoices) [MyApp.Billing]
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().HaveCount(2);
      result[0].Should().Be(new SchemaFileMigrationInfo(202602161430, "AddUsersTable", "MyApp.Data"));
      result[1].Should().Be(new SchemaFileMigrationInfo(202601010900, "AddInvoices", "MyApp.Billing"));
   }

   [Fact]
   public void ParseMigrationVersion_WithMixedLegacyAndScopedLines_ReturnsBoth()
   {
      var content = """
         --
         -- Migration version: 202602161430 (AddUsersTable)
         -- Migration version: 202601010900 (AddInvoices) [MyApp.Billing]
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().HaveCount(2);
      result[0].Scope.Should().BeNull();
      result[1].Scope.Should().Be("MyApp.Billing");
   }

   [Fact]
   public void ParseMigrationVersion_WithNoMigrationVersion_ReturnsEmpty()
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

      result.Should().BeEmpty();
   }

   [Fact]
   public void ParseMigrationVersion_WithEmptyContent_ReturnsEmpty()
   {
      var result = SchemaFileParser.ParseMigrationVersion("");

      result.Should().BeEmpty();
   }

   [Fact]
   public void ParseMigrationVersion_WithNoHeader_ReturnsEmpty()
   {
      var content = "CREATE TABLE users (id SERIAL PRIMARY KEY);";

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().BeEmpty();
   }

   [Fact]
   public void ParseMigrationVersion_WithMalformedIdentifier_ReturnsEmpty()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Migration version: not-a-number (AddUsersTable)
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().BeEmpty();
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

      result.Should().ContainSingle();
      result[0].Identifier.Should().Be(202602181500);
      result[0].Name.Should().Be("Add_Users_And_Orders_Tables_v2");
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

      result.Should().ContainSingle();
      result[0].Identifier.Should().Be(202602181500);
      result[0].Name.Should().Be("Add Users Table");
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

      result.Should().ContainSingle();
      result[0].Identifier.Should().Be(202602181500);
      result[0].Name.Should().Be("AddUsersTable");
   }

   [Fact]
   public void ParseMigrationVersion_WithExtraWhitespaceAroundScope_TrimsScope()
   {
      var content = """
         --
         -- PostgreSQL database schema
         -- Migration version: 202602181500 (AddUsersTable)   [  MyApp.Data  ]
         --
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().ContainSingle();
      result[0].Scope.Should().Be("MyApp.Data");
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

      result.Should().ContainSingle();
      result[0].Identifier.Should().Be(999912312359);
      result[0].Name.Should().Be("FinalMigration");
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

      result.Should().ContainSingle();
      result[0].Identifier.Should().Be(202602161430);
      result[0].Name.Should().Be("TestMigration");
   }

   [Fact]
   public void FormatMigrationVersion_RoundTripsThroughParse()
   {
      var content = $"""
         -- Migration version: {SchemaFileParser.FormatMigrationVersion(202602161430, "AddUsersTable", "MyApp.Data")}
         -- Migration version: {SchemaFileParser.FormatMigrationVersion(202601010900, "AddInvoices", null)}
         """;

      var result = SchemaFileParser.ParseMigrationVersion(content);

      result.Should().HaveCount(2);
      result[0].Should().Be(new SchemaFileMigrationInfo(202602161430, "AddUsersTable", "MyApp.Data"));
      result[1].Should().Be(new SchemaFileMigrationInfo(202601010900, "AddInvoices", Scope: null));
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

         result.Should().ContainSingle();
         result[0].Identifier.Should().Be(202602161430);
         result[0].Name.Should().Be("AddUsersTable");
      }
      finally
      {
         File.Delete(tempFile);
      }
   }

   [Fact]
   public async Task ParseMigrationVersionFromFileAsync_WithMissingFile_ThrowsFileNotFoundException()
   {
      var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sql");

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

         result.Should().ContainSingle();
         result[0].Identifier.Should().Be(202602161430);
         result[0].Name.Should().Be("AddUsersTable");
      }
      finally
      {
         File.Delete(tempFile);
      }
   }

   [Fact]
   public void ParseMigrationVersionFromFile_WithMissingFile_ThrowsFileNotFoundException()
   {
      var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sql");

      var action = () => SchemaFileParser.ParseMigrationVersionFromFile(nonExistentPath);

      action.Should().Throw<FileNotFoundException>();
   }
}
