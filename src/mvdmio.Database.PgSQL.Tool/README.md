# mvdmio.Database.PgSQL.Tool

CLI workflow for PostgreSQL migrations and schema files.

Install the package as a `dotnet` tool and use the `db` command to scaffold migrations, apply them, export schema files, and clean up old migration sources.

## Installation

Install globally:

```bash
dotnet tool install --global mvdmio.Database.PgSQL.Tool
```

Or install locally to a tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install mvdmio.Database.PgSQL.Tool
```

After installation, run the tool as `db`.

Targets `net8.0`, `net9.0`, and `net10.0`.

## Quick Start

```bash
db init
db migration create AddUsersTable
db migrate latest
db pull
```

## Main Commands

### `db init`

Creates a `.mvdmio-migrations.yml` file in your project.

### `db migration create <name>`

Creates a timestamped migration class you can fill in with SQL.

Example:

```bash
db migration create AddUsersTable
```

### `db migrate latest`

Applies all pending migrations.

```bash
db migrate latest
db migrate latest --environment prod
db migrate latest --connection-string "Host=localhost;Database=mydb;Username=postgres;Password=secret"
```

### `db migrate to <identifier>`

Applies migrations up to a specific version.

```bash
db migrate to 202602161430
```

### `db pull`

Exports the current database schema to a SQL file.

```bash
db pull
db pull --environment prod
```

By default, schema files are written into `Schemas/`.

### `db cleanup`

Refreshes schema files for configured environments and removes migrations that are older than every retained schema version.

```bash
db cleanup
```

## Configuration

The tool uses `.mvdmio-migrations.yml`.

```yaml
project: src/MyApp.Data
migrationsDirectory: Migrations
schemasDirectory: Schemas
connectionStrings:
  local: Host=localhost;Database=mydb;Username=postgres;Password=secret
  acc: Host=acc-server;Database=mydb;Username=postgres;Password=secret
  prod: Host=prod-server;Database=mydb;Username=postgres;Password=secret
```

Connection strings are resolved in this order:

1. `--connection-string`
2. `--environment` or `-e`
3. First entry in `connectionStrings`

## Typical Workflow

```bash
db init
db migration create AddOrdersTable
db migrate latest --environment local
db pull --environment local
```

## Companion Library

This tool is designed to work with [`mvdmio.Database.PgSQL`](../mvdmio.Database.PgSQL/README.md).

## License

MIT. See [`../../LICENSE`](../../LICENSE).
