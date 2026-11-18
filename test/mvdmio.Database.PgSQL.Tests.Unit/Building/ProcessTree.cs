using System.Diagnostics;

namespace mvdmio.Database.PgSQL.Tests.Unit.Building;

internal static class ProcessTree
{
   private const string LIST_PROCESSES_ON_WINDOWS = "Get-CimInstance Win32_Process | ForEach-Object { '{0} {1} {2}' -f $_.ProcessId, $_.ParentProcessId, $_.CommandLine }";

   public static async Task<IReadOnlyCollection<ProcessEntry>> DescendantsAsync(int rootId, CancellationToken cancellationToken)
   {
      var processes = await ListProcessesAsync(cancellationToken);
      var descendants = new List<ProcessEntry>();
      var parentIds = new Queue<int>([rootId]);

      while (parentIds.TryDequeue(out var parentId))
      {
         foreach (var child in processes.Where(x => x.ParentId == parentId && descendants.All(d => d.ProcessId != x.ProcessId)))
         {
            descendants.Add(child);
            parentIds.Enqueue(child.ProcessId);
         }
      }

      return descendants;
   }

   public static bool IsRunning(int processId)
   {
      try
      {
         using var process = Process.GetProcessById(processId);
         return !process.HasExited;
      }
      catch (ArgumentException)
      {
         return false;
      }
   }

   private static async Task<IReadOnlyCollection<ProcessEntry>> ListProcessesAsync(CancellationToken cancellationToken)
   {
      using var process = new Process
      {
         StartInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("powershell", ["-NoProfile", "-Command", LIST_PROCESSES_ON_WINDOWS])
            : new ProcessStartInfo("ps", ["-A", "-o", "pid=,ppid=,args="])
      };
      process.StartInfo.RedirectStandardOutput = true;
      process.StartInfo.UseShellExecute = false;
      process.StartInfo.CreateNoWindow = true;
      process.Start();

      var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
      await process.WaitForExitAsync(cancellationToken);

      return output
         .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
         .Select(line => line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries))
         .Where(parts => parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
         .Select(parts => new ProcessEntry(int.Parse(parts[0]), int.Parse(parts[1]), parts.Length == 3 ? parts[2] : string.Empty))
         .ToArray();
   }

   internal sealed record ProcessEntry(int ProcessId, int ParentId, string CommandLine);
}
