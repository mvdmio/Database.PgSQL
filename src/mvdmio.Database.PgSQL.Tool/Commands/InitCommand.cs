using mvdmio.Database.PgSQL.Tool.Init;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

internal static class InitCommand
{
   public static Command Create()
   {
      var handler = new InitHandler();

      var command = new Command("init", "Initialize a .mvdmio-migrations.yml configuration file in the current directory");

      command.SetAction(_ => handler.Handle(Directory.GetCurrentDirectory()));

      return command;
   }
}
