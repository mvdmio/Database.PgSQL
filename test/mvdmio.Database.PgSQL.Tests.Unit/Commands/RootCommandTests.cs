using AwesomeAssertions;
using mvdmio.Database.PgSQL.Migrations;
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

   [Theory]
   [InlineData("migrate latest --phase pre-deploy", DeployPhase.PreDeploy)]
   [InlineData("migrate latest --phase post-deploy", DeployPhase.PostDeploy)]
   [InlineData("migrate latest --phase all", DeployPhase.All)]
   [InlineData("migrate latest", DeployPhase.PreDeploy)]
   [InlineData("migrate to 202602161430 --phase pre-deploy", DeployPhase.PreDeploy)]
   [InlineData("migrate to 202602161430 --phase post-deploy", DeployPhase.PostDeploy)]
   [InlineData("migrate to 202602161430 --phase all", DeployPhase.All)]
   [InlineData("migrate to 202602161430", DeployPhase.PreDeploy)]
   public void Parse_MigrateWithPhaseOption_ParsesIntoDeployPhase(string commandLine, DeployPhase expected)
   {
      var parseResult = RootCommandFactory.Create().Parse(commandLine.Split(' '));

      parseResult.Errors.Should().BeEmpty();
      parseResult.GetValue<DeployPhase>("--phase").Should().Be(expected);
   }

   [Theory]
   [InlineData("migrate latest --phase sometimes")]
   [InlineData("migrate latest --phase PRE-DEPLOY")]
   [InlineData("migrate to 202602161430 --phase sometimes")]
   public void Invoke_MigrateWithUnknownPhase_ReportsParseErrorAndExitsWithOne(string commandLine)
   {
      var error = new StringWriter();
      Console.SetError(error);

      var exitCode = RootCommandFactory.Create().Parse(commandLine.Split(' ')).Invoke();

      exitCode.Should().Be(1);
      error.ToString().Should().Contain("not recognized for option '--phase'. Must be one of: pre-deploy, post-deploy, all.");
   }

   [Theory]
   [InlineData("migrate latest --help")]
   [InlineData("migrate to --help")]
   public void Invoke_MigrateHelp_ListsThePhaseValuesAndItsDefault(string commandLine)
   {
      var output = new StringWriter();

      var exitCode = RootCommandFactory.Create().Parse(commandLine.Split(' ')).Invoke(new InvocationConfiguration { Output = output });

      exitCode.Should().Be(0);
      output.ToString().Should().Contain("--phase").And.Contain("pre-deploy, post-deploy, all");
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
