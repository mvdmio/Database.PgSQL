using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Packaging.Fixture;
using System.Diagnostics;

namespace mvdmio.Database.PgSQL.Tests.Packaging;

public class DotnetRunTests
{
   [Fact(Timeout = 60_000)]
   public void Run_WhenAnOrphanedProcessKeepsThePipesOpen_ReturnsAFailedRunWithTheCommandLine()
   {
      var (fileName, arguments) = OperatingSystem.IsWindows()
         ? ("cmd.exe", new[] { "/c", "start /b ping -n 120 127.0.0.1" })
         : ("/bin/sh", new[] { "-c", "sleep 120 & exit 0" });
      var expectedCommandLine = string.Join(' ', arguments.Prepend(fileName));
      var stopwatch = Stopwatch.StartNew();

      var run = Dotnet.Run(fileName, arguments, Path.GetTempPath(), timeout: TimeSpan.FromSeconds(5));

      stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
      run.Succeeded.Should().BeFalse(run.Report("the shell"));
      run.Report("the shell").Should().Contain(expectedCommandLine);
      run.Report("the shell").Should().Contain("did not finish within 5 seconds and was stopped");
   }
}
