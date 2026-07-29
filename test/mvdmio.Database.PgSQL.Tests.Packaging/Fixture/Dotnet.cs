using System.Diagnostics;
using System.Text;

namespace mvdmio.Database.PgSQL.Tests.Packaging.Fixture;

/// <summary>What one child process did: whether it succeeded, and everything it wrote.</summary>
public sealed record ProcessRun(int ExitCode, string Output)
{
   public bool Succeeded => ExitCode == 0;

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

   public static ProcessRun Run(string fileName, IEnumerable<string> arguments, string workingDirectory, IDictionary<string, string?>? environment = null)
   {
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

      if (environment is not null)
      {
         foreach (var (key, value) in environment)
         {
            startInfo.Environment[key] = value;
         }
      }

      using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");

      // Read both streams before waiting: a child filling a pipe buffer while nothing drains it deadlocks, and an SDK
      // build writes more than enough to do that.
      var output = new StringBuilder();
      var standardOutput = process.StandardOutput.ReadToEndAsync();
      var standardError = process.StandardError.ReadToEndAsync();

      process.WaitForExit();
      output.Append(standardOutput.GetAwaiter().GetResult()).Append(standardError.GetAwaiter().GetResult());

      return new ProcessRun(process.ExitCode, output.ToString());
   }
}
