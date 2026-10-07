using mvdmio.Database.PgSQL.Tool.Cleanup;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db cleanup
/// </summary>
internal static class CleanupCommand
{
   public static Command Create()
   {
      var handler = new CleanupHandler();

      var command = new Command("cleanup", "Pull schemas for all environments and delete obsolete migration files");

      command.SetAction(async (_, cancellationToken) =>
      {
         await handler.HandleAsync(cancellationToken);
      });

      return command;
   }
}
