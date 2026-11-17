using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Packaging.Fixture;
using System.Diagnostics;

namespace mvdmio.Database.PgSQL.Tests.Packaging;

/// <summary>
///    That a child process which stops returning fails the run with a readable report, instead of stalling it. Does not use
///    <see cref="PackagingFixture" />: it needs neither Docker nor a pack.
/// </summary>
public class DotnetRunTests
{
   /// <remarks>
   ///    The shell exits at once and leaves a background <c>sleep</c> holding its stdout and stderr, which is the shape of
   ///    the MSBuild workers that hung the publish workflow. That <c>sleep</c> is outside the shell's process tree once the
   ///    shell has exited, so it is not killed and runs out its 120 seconds on its own; the test does not clean it up.
   /// </remarks>
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
