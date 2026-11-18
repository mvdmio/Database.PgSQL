using System.Diagnostics;
using System.Text;

namespace mvdmio.Database.PgSQL.Tests.Packaging.Fixture;

public sealed record ProcessRun(int ExitCode, string Output, string? StoppedBecause = null)
{
   public bool Succeeded => ExitCode == 0 && StoppedBecause is null;

   public static ProcessRun NotAttempted(string what)
   {
      return new ProcessRun(-1, $"{what} was not attempted, because an earlier step failed. Read that step's own failure.");
   }

   public string Report(string what)
   {
      if (StoppedBecause is not null)
         return $"{what} {StoppedBecause}{Environment.NewLine}Output so far:{Environment.NewLine}{Output}";

      return $"{what} exited with {ExitCode}:{Environment.NewLine}{Output}";
   }
}

internal static class Dotnet
{
   private const string ROLL_FORWARD_VARIABLE = "DOTNET_ROLL_FORWARD";

   private const string NODE_REUSE_VARIABLE = "MSBUILDDISABLENODEREUSE";

   private static readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(5);

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

      var output = new StringBuilder();
      var finished = Task.WhenAll(Drain(process.StandardOutput, output), Drain(process.StandardError, output), process.WaitForExitAsync());

      if (finished.Wait(limit))
         return new ProcessRun(process.ExitCode, output.ToString());

      if (!process.HasExited)
         process.Kill(entireProcessTree: true);

      var commandLine = string.Join(' ', startInfo.ArgumentList.Prepend(fileName));

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
