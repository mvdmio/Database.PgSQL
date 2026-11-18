using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

internal static class CancellableCommandExtensions
{
   public static void SetCancellableAction(this Command command, Func<ParseResult, CancellationToken, Task<int>> action)
   {
      command.SetAction(async (parseResult, cancellationToken) =>
      {
         try
         {
            return await action(parseResult, cancellationToken);
         }
         catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
         {
            Console.Error.WriteLine("Cancelled.");
            return ExitCodes.CANCELLED;
         }
      });
   }
}
