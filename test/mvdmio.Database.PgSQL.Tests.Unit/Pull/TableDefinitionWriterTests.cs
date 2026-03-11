using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Schema.Models;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Pull;

namespace mvdmio.Database.PgSQL.Tests.Unit.Pull;

public class TableDefinitionWriterTests : IDisposable
{
   private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"mvdmio-pull-tests-{Guid.NewGuid():N}");

   [Fact]
   public async Task WriteAsync_WritesGeneratedTableDefinitionsIntoTablesDirectory()
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      Directory.CreateDirectory(Path.Combine(_tempDirectory, "src", "MyApp"));
      await File.WriteAllTextAsync(
         Path.Combine(_tempDirectory, "src", "MyApp", "MyApp.csproj"),
         "<Project><PropertyGroup><RootNamespace>Demo.App</RootNamespace></PropertyGroup></Project>",
         cancellationToken
      );

      var writer = new TableDefinitionWriter();
      var config = new ToolConfiguration
      {
         BasePath = _tempDirectory,
         Project = Path.Combine("src", "MyApp", "MyApp.csproj")
      };

      var result = await writer.WriteAsync(
         config,
         [
            new TableInfo
            {
               Schema = "public",
               Name = "users",
               Columns =
               [
                  new ColumnInfo { Name = "user_id", DataType = "bigint", IsNullable = false, IsIdentity = true, IdentityGeneration = "ALWAYS" },
                  new ColumnInfo { Name = "user_name", DataType = "text", IsNullable = false, IsIdentity = false }
               ]
            }
         ],
         [new ConstraintInfo { Schema = "public", TableName = "users", ConstraintName = "pk_users", ConstraintType = "p", Definition = "PRIMARY KEY (user_id)" }],
         cancellationToken
      );

      result.TablesDirectory.Should().Be(Path.Combine(_tempDirectory, "src", "MyApp", "Tables"));
      result.GeneratedFileCount.Should().Be(1);
      result.Warnings.Should().BeEmpty();

      var generatedFilePath = Path.Combine(_tempDirectory, "src", "MyApp", "Tables", "UsersTable.cs");
      File.Exists(generatedFilePath).Should().BeTrue();
      var generatedContent = await File.ReadAllTextAsync(generatedFilePath, cancellationToken);
      generatedContent.Should().Contain("namespace Demo.App.Tables;");
      generatedContent.Should().Contain("[Table(\"public.users\")]");
      generatedContent.Should().Contain("public long UserId { get; set; }");
   }

   public void Dispose()
   {
      if (Directory.Exists(_tempDirectory))
         Directory.Delete(_tempDirectory, true);
   }
}
