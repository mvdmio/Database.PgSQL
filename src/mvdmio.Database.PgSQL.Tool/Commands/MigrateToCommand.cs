using mvdmio.Database.PgSQL.Tool.Migrations;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db migrate to &lt;identifier&gt;
/// </summary>
internal static class MigrateToCommand
{
   public static Command Create()
   {
      var handler = new MigrateHandler();

      var identifierArgument = new Argument<long>("identifier")
      {
         Description = "The migration identifier to migrate up to (inclusive), e.g. 202602161430"
      };

      var connectionStringOption = new Option<string?>("--connection-string")
      {
         Description = "Override the connection string from the configuration file"
      };

      var environmentOption = new Option<string?>("--environment", "-e")
      {
         Description = "The environment to use (looks up the connection string from connectionStrings in .mvdmio-migrations.yml)"
      };

      var command = new Command("to", "Migrate the database up to a specific version");
      command.Arguments.Add(identifierArgument);
      command.Options.Add(connectionStringOption);
      command.Options.Add(environmentOption);

      command.SetAction(async (parseResult, cancellationToken) =>
      {
         await handler.HandleAsync(
            MigrateRequest.To(parseResult.GetValue(identifierArgument)),
            parseResult.GetValue(connectionStringOption),
            parseResult.GetValue(environmentOption),
            cancellationToken
         );
      });

      return command;
   }
}
