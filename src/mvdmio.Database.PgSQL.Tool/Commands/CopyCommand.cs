using mvdmio.Database.PgSQL.Tool.Copy;
using System.CommandLine;

namespace mvdmio.Database.PgSQL.Tool.Commands;

/// <summary>
///    Command: db copy --from &lt;env&gt; --to &lt;env&gt;
/// </summary>
internal static class CopyCommand
{
   public static Command Create()
   {
      var handler = new CopyHandler();

      var fromOption = new Option<string>("--from", "-f")
      {
         Description = "Source environment name (key in connectionStrings of .mvdmio-migrations.yml)",
         Required = true
      };

      var toOption = new Option<string>("--to", "-t")
      {
         Description = "Destination environment name (key in connectionStrings of .mvdmio-migrations.yml)",
         Required = true
      };

      var schemasOption = new Option<string[]?>("--schemas")
      {
         Description = "Override the schemas list from configuration. Comma-separated or repeated.",
         AllowMultipleArgumentsPerToken = true
      };

      var excludeTablesOption = new Option<string[]?>("--exclude-tables")
      {
         Description = "Fully-qualified schema.table entries to skip. Comma-separated or repeated.",
         AllowMultipleArgumentsPerToken = true
      };

      var command = new Command("copy", "Copy all table data from one environment's database to another (truncates destination tables).");
      command.Options.Add(fromOption);
      command.Options.Add(toOption);
      command.Options.Add(schemasOption);
      command.Options.Add(excludeTablesOption);

      command.SetAction(async (parseResult, cancellationToken) =>
      {
         var schemas = ExpandCsv(parseResult.GetValue(schemasOption));
         var excludeTables = ExpandCsv(parseResult.GetValue(excludeTablesOption));

         await handler.HandleAsync(
            parseResult.GetValue(fromOption)!,
            parseResult.GetValue(toOption)!,
            schemas,
            excludeTables,
            cancellationToken
         );
      });

      return command;
   }

   private static IReadOnlyCollection<string>? ExpandCsv(string[]? values)
   {
      if (values is null || values.Length == 0)
         return null;

      var result = values
         .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
         .Where(v => !string.IsNullOrWhiteSpace(v))
         .ToArray();

      return result.Length == 0 ? null : result;
   }
}
