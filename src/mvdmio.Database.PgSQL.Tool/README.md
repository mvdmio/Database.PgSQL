# mvdmio.Database.PgSQL.Tool

A CLI tool for managing PostgreSQL database migrations. Part of the [mvdmio.Database.PgSQL](../mvdmio.Database.PgSQL/README.md) library.

Targets .NET 8.0, .NET 9.0, and .NET 10.0.

## Installation

```bash
dotnet tool install --global mvdmio.Database.PgSQL.Tool
```

After installation, the tool is available as `db`.

## Quick Start

```bash
# Initialize a configuration file in your project
db init

# Create a new migration
db migration create AddUsersTable

# Run all pending migrations
db migrate latest

# Pull the current database schema
db pull
```

## Commands

### `db init`

Creates a `.mvdmio-migrations.yml` configuration file in the current directory with default settings.

```bash
db init
```

Output:
```
Created configuration file: .mvdmio-migrations.yml

Default settings:
  project:             .
  migrationsDirectory: Migrations
  schemasDirectory:    Schemas
  connectionStrings:   local (placeholder)

Migrations are tracked in the 'mvdmio.migrations' table.
```

### `db migration create <name>`

Scaffolds a new migration file with a timestamp-based identifier.

```bash
db migration create AddUsersTable
```

Output:
```
Created migration: Migrations/_202602191430_AddUsersTable.cs
  Identifier: 202602191430
  Name:       AddUsersTable
  Namespace:  MyApp.Migrations
```

Generated file:

```csharp
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace MyApp.Migrations;

public class _202602191430_AddUsersTable : IDbMigration
{
   public long Identifier { get; } = 202602191430;
   public string Name { get; } = "AddUsersTable";

   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         -- TODO: Write your migration SQL here
         """
      );
   }
}
```

The namespace is automatically resolved from the nearest `.csproj` file's `<RootNamespace>` property (or project file name if not set), combined with the relative path to the migrations directory.

### `db migrate latest`

Runs all pending migrations to bring the database to the latest version.

```bash
# Use the default environment (first in connectionStrings)
db migrate latest

# Use a specific environment
db migrate latest --environment prod
db migrate latest -e acc

# Override with an explicit connection string
db migrate latest --connection-string "Host=localhost;Database=mydb;..."
```

The command:
1. Builds the configured project
2. Loads migrations from the built assembly
3. Compares with already-executed migrations
4. Applies pending migrations in order

When an empty database is detected and an embedded schema file exists, the schema is applied instead of running all migrations individually. See [Schema-First Migrations](#schema-first-migrations).

### `db migrate to <identifier>`

Migrates the database up to a specific version (inclusive).

```bash
db migrate to 202602161430

# With environment
db migrate to 202602161430 --environment prod
```

### `db pull`

Extracts the current database schema and saves it as a SQL file.

```bash
# Pull from the default environment
db pull

# Pull from a specific environment
db pull --environment prod
db pull -e acc

# Override with an explicit connection string
db pull --connection-string "Host=localhost;Database=mydb;..."
```

The schema is written to the `Schemas/` directory (configurable via `schemasDirectory`):
- With an environment: `schema.<environment>.sql` (e.g., `schema.local.sql`)
- Without an environment: `schema.sql`

The generated schema file includes:
- Extensions
- Schemas (excluding system schemas)
- Enum, composite, and domain types
- Sequences
- Tables with columns, constraints, and indexes
- Functions and procedures
- Triggers
- Views
- A header comment with the current migration version

## Configuration

The `.mvdmio-migrations.yml` file configures the tool:

```yaml
# Path to the project containing migrations (relative to this file)
project: src/MyApp.Data

# Directory for new migration files (relative to this file)
migrationsDirectory: Migrations

# Directory for schema files from db pull (relative to this file)
schemasDirectory: Schemas

# Named connection strings
connectionStrings:
  local: Host=localhost;Database=mydb;Username=postgres;Password=secret
  acc: Host=acc-server;Database=mydb;Username=postgres;Password=secret
  prod: Host=prod-server;Database=mydb;Username=postgres;Password=secret
```

Migrations are tracked in the `mvdmio.migrations` table (automatically created).

The configuration file is searched from the current directory upward, allowing you to run the tool from any subdirectory of your project.

### Connection String Resolution

Connection strings are resolved in this order:
1. `--connection-string` flag (explicit override)
2. `--environment` / `-e` flag (looks up from `connectionStrings`)
3. First entry in `connectionStrings` (fallback)
4. Error if none resolve

## Schema-First Migrations

For new database instances, the migrator can apply an embedded schema file instead of running all migrations individually. This significantly speeds up provisioning.

### Workflow

1. After running migrations on your production database, pull the schema:
   ```bash
   db pull --environment prod
   ```

2. The schema file (`Schemas/schema.prod.sql`) is automatically embedded as an assembly resource on build.

3. When migrating an empty database:
   ```bash
   db migrate latest --environment local
   ```
   The migrator detects the empty database, applies the embedded schema, records the migration version from the schema header, then runs any newer migrations.

### How It Works

Schema files are automatically embedded via a `.props` file included in the NuGet package. Any `.sql` file in the `Schemas/` directory is embedded as an assembly resource.

When the migrator encounters an empty database:
1. It scans for embedded schema resources
2. For environment-based runs, it looks for `schema.<environment>.sql`
3. Falls back to `schema.sql` if no environment-specific file exists
4. If found, applies the schema and records the migration version from the header
5. Runs any migrations newer than the schema version

## Project Structure

```
mvdmio.Database.PgSQL.Tool/
├── Building/
│   └── ProjectBuilder.cs          # Builds projects and loads assemblies
├── Commands/
│   ├── InitCommand.cs             # db init
│   ├── MigrationCreateCommand.cs  # db migration create
│   ├── MigrateLatestCommand.cs    # db migrate latest
│   ├── MigrateToCommand.cs        # db migrate to
│   └── PullCommand.cs             # db pull
├── Configuration/
│   └── ToolConfiguration.cs       # YAML config loading/saving
├── Scaffolding/
│   ├── MigrationScaffolder.cs     # Generates migration file content
│   └── NamespaceResolver.cs       # Resolves namespace from .csproj
└── Program.cs                     # CLI entry point
```

## Dependencies

- [System.CommandLine](https://github.com/dotnet/command-line-api) - Command-line parsing
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) - YAML configuration parsing
- [mvdmio.Database.PgSQL](../mvdmio.Database.PgSQL/README.md) - Database operations and migrations

## License

MIT -- see [LICENSE](../../LICENSE) for details.
