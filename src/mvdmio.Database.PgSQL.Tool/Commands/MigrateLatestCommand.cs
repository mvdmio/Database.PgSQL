using mvdmio.Database.PgSQL.Tool.Migrations;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db migrate latest
/// </summary>
internal static class MigrateLatestCommand
{
   public static Command Create()
   {
      var handler = new MigrateHandler();

      var connectionStringOption = new Option<string?>("--connection-string")
      {
         Description = "Override the connection string from the configuration file"
      };

      var environmentOption = new Option<string?>("--environment", "-e")
      {
         Description = "The environment to use (looks up the connection string from connectionStrings in .mvdmio-migrations.yml)"
      };

      var command = new Command("latest", "Migrate the database to the latest version");
      command.Options.Add(connectionStringOption);
      command.Options.Add(environmentOption);

      command.SetAction(async (parseResult, cancellationToken) =>
      {
         await handler.HandleAsync(
            MigrateRequest.Latest,
            parseResult.GetValue(connectionStringOption),
            parseResult.GetValue(environmentOption),
            cancellationToken
         );
      });

      return command;
   }
}
