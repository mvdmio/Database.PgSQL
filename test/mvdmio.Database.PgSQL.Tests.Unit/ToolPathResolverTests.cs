using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tool.Configuration;

namespace mvdmio.Database.PgSQL.Tests.Unit;

public class ToolPathResolverTests
{
   [Fact]
   public void GetProjectPath_ReturnsAbsolutePath()
   {
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         Project = Path.Combine("src", "MyApp.Data")
      };

      var result = ToolPathResolver.GetProjectPath(config);

      result.Should().Be(Path.GetFullPath(Path.Combine("C:", "repo", "src", "MyApp.Data")));
   }

   [Fact]
   public void GetProjectDirectoryPath_WithProjectDirectory_ReturnsDirectory()
   {
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo")
      };

      var result = ToolPathResolver.GetProjectDirectoryPath(config);

      result.Should().Be(Path.GetFullPath(Path.Combine("C:", "repo")));
   }

   [Fact]
   public void GetProjectDirectoryPath_WithCsprojPath_ReturnsProjectDirectory()
   {
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         Project = Path.Combine("src", "MyApp.Data", "MyApp.Data.csproj")
      };

      var result = ToolPathResolver.GetProjectDirectoryPath(config);

      result.Should().Be(Path.GetFullPath(Path.Combine("C:", "repo", "src", "MyApp.Data")));
   }

   [Fact]
   public void GetMigrationsDirectoryPath_ReturnsAbsolutePath()
   {
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         MigrationsDirectory = "Migrations"
      };

      var result = ToolPathResolver.GetMigrationsDirectoryPath(config);

      result.Should().Be(Path.GetFullPath(Path.Combine("C:", "repo", "Migrations")));
   }

   [Fact]
   public void GetSchemasDirectoryPath_ReturnsAbsolutePath()
   {
      var config = new ToolConfiguration
      {
         BasePath = Path.Combine("C:", "repo"),
         SchemasDirectory = "Schemas"
      };

      var result = ToolPathResolver.GetSchemasDirectoryPath(config);

      result.Should().Be(Path.GetFullPath(Path.Combine("C:", "repo", "Schemas")));
   }
}
