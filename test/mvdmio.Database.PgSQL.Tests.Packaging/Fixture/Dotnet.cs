using System.Diagnostics;
using System.Text;

namespace mvdmio.Database.PgSQL.Tests.Packaging.Fixture;

/// <summary>
///    What one child process did: whether it succeeded, and everything it wrote. A child that did not finish in time
///    carries <paramref name="StoppedBecause" /> and never succeeded.
/// </summary>
public sealed record ProcessRun(int ExitCode, string Output, string? StoppedBecause = null)
{
   public bool Succeeded => ExitCode == 0 && StoppedBecause is null;

   /// <summary>
   ///    A step that never ran, because an earlier one failed. Fails like any other failed step, and says which.
   /// </summary>
   public static ProcessRun NotAttempted(string what)
   {
      return new ProcessRun(-1, $"{what} was not attempted, because an earlier step failed. Read that step's own failure.");
   }

   /// <summary>The output with the command that produced it, so a failed assertion reads like the console would.</summary>
   public string Report(string what)
   {
      if (StoppedBecause is not null)
         return $"{what} {StoppedBecause}{Environment.NewLine}Output so far:{Environment.NewLine}{Output}";

      return $"{what} exited with {ExitCode}:{Environment.NewLine}{Output}";
   }
}

/// <summary>
///    Runs the SDK and the scaffolded consumer as child processes, because that is the only way to observe a package
///    being installed and a generator running inside another compilation.
/// </summary>
internal static class Dotnet
{
   /// <summary>
   ///    Roll-forward is allowed for every child process here. The consumer targets three frameworks and this machine may
   ///    have only the newest runtime installed; where the matching one is present this changes nothing, and where it is
   ///    not, the framework-specific assembly chosen at build time is still the one that loads — which is the thing under
   ///    test.
   /// </summary>
   private const string ROLL_FORWARD_VARIABLE = "DOTNET_ROLL_FORWARD";

   /// <summary>
   ///    Node reuse is off for every child process here. MSBuild workers that outlive their build inherit the child's
   ///    redirected stdout and stderr and keep them open, so the pipes never close and <see cref="Run" /> would wait on
   ///    them forever. With reuse off, MSBuild stops its workers when the build ends.
   /// </summary>
   private const string NODE_REUSE_VARIABLE = "MSBUILDDISABLENODEREUSE";

   private static readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(5);

   /// <param name="timeout">
   ///    One budget, counted from the child's start, for the child to exit and for both its pipes to close. Past it the
   ///    child's process tree is killed and the returned run has failed.
   /// </param>
   public static ProcessRun Run(string fileName, IEnumerable<string> arguments, string workingDirectory, IDictionary<string, string?>? environment = null, TimeSpan? timeout = null)
   {
      var limit = timeout ?? _defaultTimeout;
      var startInfo = new ProcessStartInfo(fileName)
      {
         WorkingDirectory = workingDirectory,
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         UseShellExecute = false
      };

      foreach (var argument in arguments)
      {
         startInfo.ArgumentList.Add(argument);
      }

      startInfo.Environment[ROLL_FORWARD_VARIABLE] = "LatestMajor";
      startInfo.Environment[NODE_REUSE_VARIABLE] = "1";

      if (environment is not null)
      {
         foreach (var (key, value) in environment)
         {
            startInfo.Environment[key] = value;
         }
      }

      using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");

      // Read both streams while waiting: a child filling a pipe buffer while nothing drains it deadlocks, and an SDK
      // build writes more than enough to do that. Into one shared buffer as it arrives, so that what a child wrote
      // before it was stopped can still be reported.
      var output = new StringBuilder();
      var finished = Task.WhenAll(Drain(process.StandardOutput, output), Drain(process.StandardError, output), process.WaitForExitAsync());

      if (finished.Wait(limit))
         return new ProcessRun(process.ExitCode, output.ToString());

      // A child that already exited has nothing left to kill: only a process it left behind is holding the pipes, and
      // that one is outside the tree.
      if (!process.HasExited)
         process.Kill(entireProcessTree: true);

      var commandLine = string.Join(' ', startInfo.ArgumentList.Prepend(fileName));

      // The drains are abandoned here, still reading; disposing the process fails them, and nothing awaits them.
      lock (output)
      {
         return new ProcessRun(-1, output.ToString(), $"did not finish within {limit.TotalSeconds:0.#} seconds and was stopped: {commandLine}");
      }
   }

   private static async Task Drain(StreamReader reader, StringBuilder output)
   {
      var chunk = new char[4096];
      int read;

      while ((read = await reader.ReadAsync(chunk)) > 0)
      {
         lock (output)
         {
            output.Append(chunk, 0, read);
         }
      }
   }
}
