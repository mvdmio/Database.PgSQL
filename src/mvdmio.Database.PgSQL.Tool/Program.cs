using mvdmio.Database.PgSQL.Tool.Commands;
using System.CommandLine;

var rootCommand = new RootCommand("PostgreSQL database migration tool");

rootCommand.Subcommands.Add(InitCommand.Create());

rootCommand.Subcommands.Add(PullCommand.Create());

rootCommand.Subcommands.Add(CleanupCommand.Create());

rootCommand.Subcommands.Add(CopyCommand.Create());

var migrationCommand = new Command("migration", "Migration scaffolding commands");
migrationCommand.Subcommands.Add(MigrationCreateCommand.Create());
rootCommand.Subcommands.Add(migrationCommand);

var migrateCommand = new Command("migrate", "Run database migrations");
migrateCommand.Subcommands.Add(MigrateLatestCommand.Create());
migrateCommand.Subcommands.Add(MigrateToCommand.Create());
rootCommand.Subcommands.Add(migrateCommand);

return rootCommand.Parse(args).Invoke();
