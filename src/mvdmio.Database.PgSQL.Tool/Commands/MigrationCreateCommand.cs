using mvdmio.Database.PgSQL.Tool.Configuration;
using mvdmio.Database.PgSQL.Tool.Scaffolding;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db migration create &lt;name&gt;
/// </summary>
internal static class MigrationCreateCommand
{
   public static Command Create()
   {
      var nameArgument = new Argument<string>("name")
      {
         Description = "The name for the new migration (e.g. AddUsersTable)"
      };

      var command = new Command("create", "Create a new migration file");
      command.Arguments.Add(nameArgument);

      command.SetAction(parseResult =>
      {
         var name = parseResult.GetValue(nameArgument)!;

         var config = ToolConfiguration.Load();
         var migrationsDir = config.GetMigrationsDirectoryPath();

         // Ensure migrations directory exists
         Directory.CreateDirectory(migrationsDir);

         var identifier = MigrationScaffolder.GenerateIdentifier();
         var migrationNamespace = NamespaceResolver.Resolve(migrationsDir);
         var content = MigrationScaffolder.GenerateContent(migrationNamespace, identifier, name);
         var fileName = MigrationScaffolder.GenerateFileName(identifier, name);
         var filePath = Path.Combine(migrationsDir, fileName);

         File.WriteAllText(filePath, content);

         Console.WriteLine($"Created migration: {filePath}");
         Console.WriteLine($"  Identifier: {identifier}");
         Console.WriteLine($"  Name:       {name}");
         Console.WriteLine($"  Namespace:  {migrationNamespace}");
      });

      return command;
   }
}
