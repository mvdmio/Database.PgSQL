using mvdmio.Database.PgSQL.Tool.Configuration;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db init
/// </summary>
internal static class InitCommand
{
   public static Command Create()
   {
      var command = new Command("init", "Initialize a .mvdmio-migrations.yml configuration file in the current directory");

      command.SetAction(_ =>
      {
         var currentDir = Directory.GetCurrentDirectory();
         var configPath = Path.Combine(currentDir, ToolConfiguration.CONFIG_FILE_NAME);

         if (File.Exists(configPath))
         {
            Console.Error.WriteLine($"Error: Configuration file already exists: {configPath}");
            return;
         }

         var config = new ToolConfiguration
         {
            ConnectionStrings = new Dictionary<string, string>
            {
               ["local"] = "Host=localhost;Database=mydb;Username=postgres;Password=secret"
            }
         };

         config.Save(currentDir);

         Console.WriteLine($"Created configuration file: {configPath}");
         Console.WriteLine();
         Console.WriteLine("Default settings:");
         Console.WriteLine($"  project:             {config.Project}");
         Console.WriteLine($"  migrationsDirectory: {config.MigrationsDirectory}");
         Console.WriteLine($"  connectionStrings:   local (placeholder)");
         Console.WriteLine();
         Console.WriteLine("Edit the file to configure your project settings and connection strings.");
         Console.WriteLine("The first configured environment is used by default when no --environment flag is passed.");
      });

      return command;
   }
}
