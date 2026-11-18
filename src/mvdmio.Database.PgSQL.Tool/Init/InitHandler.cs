using mvdmio.Database.PgSQL.Tool.Configuration;

namespace mvdmio.Database.PgSQL.Tool.Init;

internal sealed class InitHandler
{
   private readonly IInitReporter _reporter;

   public InitHandler()
      : this(new ConsoleInitReporter())
   {
   }

   internal InitHandler(IInitReporter reporter)
   {
      _reporter = reporter;
   }

   public int Handle(string directory)
   {
      var configPath = Path.Combine(directory, ToolConfiguration.CONFIG_FILE_NAME);

      if (File.Exists(configPath))
      {
         _reporter.WriteError($"Error: Configuration file already exists: {configPath}");
         return ExitCodes.FAILURE;
      }

      var config = new ToolConfiguration
      {
         ConnectionStrings = new Dictionary<string, string>
         {
            ["local"] = "Host=localhost;Database=mydb;Username=postgres;Password=secret"
         }
      };

      config.Save(directory);

      _reporter.WriteInfo($"Created configuration file: {configPath}");
      _reporter.WriteInfo(string.Empty);
      _reporter.WriteInfo("Default settings:");
      _reporter.WriteInfo($"  project:             {config.Project}");
      _reporter.WriteInfo($"  migrationsDirectory: {config.MigrationsDirectory}");
      _reporter.WriteInfo($"  schemasDirectory:    {config.SchemasDirectory}");
      _reporter.WriteInfo("  schemas:             all user schemas (default)");
      _reporter.WriteInfo("  scopes:              all migration scopes (default; declare ownership on shared databases)");
      _reporter.WriteInfo("  connectionStrings:   local (placeholder)");
      _reporter.WriteInfo(string.Empty);
      _reporter.WriteInfo("Migrations are tracked in the \"mvdmio\".\"migrations\" table.");
      _reporter.WriteInfo(string.Empty);
      _reporter.WriteInfo("Edit the file to configure your project settings and connection strings.");
      _reporter.WriteInfo("The first configured environment is used by default when no --environment flag is passed.");

      return ExitCodes.SUCCESS;
   }
}

internal interface IInitReporter
{
   void WriteInfo(string message);
   void WriteError(string message);
}

internal sealed class ConsoleInitReporter : IInitReporter
{
   public void WriteInfo(string message)
   {
      Console.WriteLine(message);
   }

   public void WriteError(string message)
   {
      Console.Error.WriteLine(message);
   }
}
