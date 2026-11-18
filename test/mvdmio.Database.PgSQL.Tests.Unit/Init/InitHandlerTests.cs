using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Init;

namespace mvdmio.Database.PgSQL.Tests.Unit.Init;

public sealed class InitHandlerTests : IDisposable
{
   private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mvdmio-init-tests-{Guid.NewGuid():N}");
   private readonly FakeInitReporter _reporter = new();

   public InitHandlerTests()
   {
      Directory.CreateDirectory(_directory);
   }

   [Fact]
   public void Handle_WithNoConfigurationFile_CreatesItAndReturnsZero()
   {
      var configPath = Path.Combine(_directory, ToolConfiguration.CONFIG_FILE_NAME);

      var exitCode = new InitHandler(_reporter).Handle(_directory);

      exitCode.Should().Be(0);
      File.Exists(configPath).Should().BeTrue();
      _reporter.Infos.Should().Contain($"Created configuration file: {configPath}");
      _reporter.Errors.Should().BeEmpty();
   }

   [Fact]
   public void Handle_WithExistingConfigurationFile_WritesErrorLeavesFileUnchangedAndReturnsOne()
   {
      var configPath = Path.Combine(_directory, ToolConfiguration.CONFIG_FILE_NAME);
      File.WriteAllText(configPath, "project: Existing");

      var exitCode = new InitHandler(_reporter).Handle(_directory);

      exitCode.Should().Be(1);
      File.ReadAllText(configPath).Should().Be("project: Existing");
      _reporter.Errors.Should().ContainSingle().Which.Should().Be($"Error: Configuration file already exists: {configPath}");
      _reporter.Infos.Should().BeEmpty();
   }

   public void Dispose()
   {
      Directory.Delete(_directory, recursive: true);
   }

   private sealed class FakeInitReporter : IInitReporter
   {
      public List<string> Infos { get; } = [];
      public List<string> Errors { get; } = [];

      public void WriteInfo(string message)
      {
         Infos.Add(message);
      }

      public void WriteError(string message)
      {
         Errors.Add(message);
      }
   }
}
