using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Unit.Building;
using mvdmio.Database.PgSQL.Tool.Commands;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tests.Unit.Commands;

[Collection(nameof(ConsoleCollection))]
public sealed class RootCommandTests : IDisposable
{
   private readonly string _originalDirectory = Directory.GetCurrentDirectory();
   private readonly TextWriter _originalError = Console.Error;
   private readonly string _emptyDirectory = Path.Combine(Path.GetTempPath(), $"mvdmio-rootcommand-tests-{Guid.NewGuid():N}");

   public RootCommandTests()
   {
      Directory.CreateDirectory(_emptyDirectory);
      Directory.SetCurrentDirectory(_emptyDirectory);
   }

   [Fact]
   public void Invoke_MigrateLatestWithNoConfigurationAndNoConnectionString_ExitsWithOne()
   {
      var error = new StringWriter();
      Console.SetError(error);

      var exitCode = RootCommandFactory.Create().Parse(["migrate", "latest"]).Invoke();

      exitCode.Should().Be(1);
      error.ToString().Should().Contain("Error: No connection string provided.");
   }

   [Fact]
   public void Invoke_InitInDirectoryHoldingAConfigurationFile_ExitsWithOneAndLeavesTheFileUnchanged()
   {
      var configPath = Path.Combine(_emptyDirectory, ".mvdmio-migrations.yml");
      File.WriteAllText(configPath, "project: Existing");
      var error = new StringWriter();
      Console.SetError(error);

      var exitCode = RootCommandFactory.Create().Parse(["init"]).Invoke();

      exitCode.Should().Be(1);
      File.ReadAllText(configPath).Should().Be("project: Existing");
      error.ToString().Should().Contain("Error: Configuration file already exists:");
   }

   [Theory]
   [InlineData("migrate latest --environment dev")]
   [InlineData("migrate to 202602161430 --environment dev")]
   [InlineData("pull --environment dev")]
   [InlineData("cleanup")]
   [InlineData("copy --from dev --to prod")]
   public async Task InvokeAsync_WithACancelledToken_WritesCancelledAndExitsWith130(string commandLine)
   {
      File.WriteAllText(
         Path.Combine(_emptyDirectory, ".mvdmio-migrations.yml"),
         "connectionStrings:\n  dev: Host=dev;Database=app\n  prod: Host=prod;Database=app\n"
      );
      var error = new StringWriter();
      Console.SetError(error);
      using var cancellation = new CancellationTokenSource();
      await cancellation.CancelAsync();

      var exitCode = await RootCommandFactory.Create().Parse(commandLine.Split(' ')).InvokeAsync(new InvocationConfiguration(), cancellation.Token);

      exitCode.Should().Be(130);
      error.ToString().Should().Be($"Cancelled.{Environment.NewLine}");
   }

   [Fact]
   public async Task InvokeAsync_WhenAnActionThrowsOperationCanceledExceptionWithoutACancelledToken_DoesNotReportCancelled()
   {
      var error = new StringWriter();
      Console.SetError(error);
      var command = new RootCommand();
      command.SetCancellableAction((_, _) => throw new OperationCanceledException());

      var action = () => command.Parse([]).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, TestContext.Current.CancellationToken);

      await action.Should().ThrowAsync<OperationCanceledException>();
      error.ToString().Should().BeEmpty();
   }

   public void Dispose()
   {
      Console.SetError(_originalError);
      Directory.SetCurrentDirectory(_originalDirectory);
      Directory.Delete(_emptyDirectory, recursive: true);
   }
}
