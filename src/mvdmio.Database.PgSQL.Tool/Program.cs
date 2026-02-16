using System.CommandLine;
using mvdmio.Database.PgSQL.Tool.Commands;

var rootCommand = new RootCommand("PostgreSQL database migration tool");

// db init
rootCommand.Subcommands.Add(InitCommand.Create());

// db migration create <name>
var migrationCommand = new Command("migration", "Migration scaffolding commands");
migrationCommand.Subcommands.Add(MigrationCreateCommand.Create());
rootCommand.Subcommands.Add(migrationCommand);

// db migrate latest | db migrate to <identifier>
var migrateCommand = new Command("migrate", "Run database migrations");
migrateCommand.Subcommands.Add(MigrateLatestCommand.Create());
migrateCommand.Subcommands.Add(MigrateToCommand.Create());
rootCommand.Subcommands.Add(migrateCommand);

return rootCommand.Parse(args).Invoke();
