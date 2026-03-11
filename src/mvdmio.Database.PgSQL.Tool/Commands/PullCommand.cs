using mvdmio.Database.PgSQL.Tool.Pull;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db pull
/// </summary>
internal static class PullCommand
{
   public static Command Create()
   {
      var handler = new PullHandler();

      var connectionStringOption = new Option<string?>("--connection-string")
      {
         Description = "Override the connection string from the configuration file"
      };

      var environmentOption = new Option<string?>("--environment", "-e")
      {
         Description = "The environment to use (looks up the connection string from connectionStrings in .mvdmio-migrations.yml)"
      };

      var command = new Command("pull", "Pull the database schema and save it as a schema.<env>.sql file");
      command.Options.Add(connectionStringOption);
      command.Options.Add(environmentOption);

      command.SetAction(async (parseResult, cancellationToken) =>
      {
         await handler.HandleAsync(
            parseResult.GetValue(connectionStringOption),
            parseResult.GetValue(environmentOption),
            cancellationToken
         );
      });

      return command;
   }
}
