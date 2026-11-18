using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Packaging.Fixture;
using System.Diagnostics;

namespace mvdmio.Database.PgSQL.Tests.Packaging;

public class DotnetRunTests
{
   [Fact(Timeout = 60_000)]
   public void Run_WhenAnOrphanedProcessKeepsThePipesOpen_ReturnsAFailedRunWithTheCommandLine()
   {
      var stopwatch = Stopwatch.StartNew();

      var run = Dotnet.Run("/bin/sh", ["-c", "sleep 120 & exit 0"], Path.GetTempPath(), timeout: TimeSpan.FromSeconds(5));

      stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
      run.Succeeded.Should().BeFalse(run.Report("the shell"));
      run.Report("the shell").Should().Contain("/bin/sh -c sleep 120 & exit 0");
      run.Report("the shell").Should().Contain("did not finish within 5 seconds and was stopped");
   }
}
