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

The exported header records one `-- Migration version: <id> (<name>) [<scope>]` line per migration scope, so a schema-first bootstrap can establish the correct baseline for every scope. Older scope-less schema files are still read.

Set `schemas` in `.mvdmio-migrations.yml` to export only specific PostgreSQL schemas. When omitted or empty, `db pull` and `db cleanup` export all user schemas. `public` is only included when listed explicitly.

Exported table definitions preserve PostgreSQL identity columns and `GENERATED ALWAYS AS (...) STORED` columns.

### `db cleanup`

Refreshes schema files for configured environments and removes migrations that are older than every retained schema version. When a schema header carries multiple per-scope version lines, the lowest one is used as the conservative deletion bound.

```bash
db cleanup
```

### `db copy`

Copies all table data from one configured environment to another using PostgreSQL binary `COPY`. Typical use case: refresh a local or test database from production.

```bash
db copy --from prod --to local
db copy -f prod -t test --schemas billing,identity
db copy -f prod -t local --exclude-tables public.audit_log,billing.large_archive
```

Behavior:

- Resolves `--from` and `--to` against `connectionStrings` in `.mvdmio-migrations.yml`. Both flags are required.
- Refuses to run when `--from` and `--to` resolve to the same connection string.
- Validates that every source table exists on the destination with at least the same columns. Missing tables or columns are reported up front and the copy is aborted.
- Truncates all destination tables in a single `TRUNCATE ... RESTART IDENTITY CASCADE` statement, then streams data table-by-table via binary `COPY`.
- Disables FK and trigger checks on the destination for the duration of the copy by setting `session_replication_role = replica`. Requires the destination user to be a superuser.
- Skips columns that are `GENERATED ... STORED` or `IDENTITY ALWAYS`. Tables where every column is filtered out are skipped with a warning.
- After the copy, advances identity / serial sequences on the destination so the next insert continues past `MAX(id)`.
- Honors the `schemas` config value (and the `--schemas` override) when both selecting tables and resetting sequences. When omitted, all user schemas are copied (system schemas and the `mvdmio.migrations` history table are always excluded).
- `--exclude-tables` accepts a comma-separated list of `schema.table` entries to skip, regardless of `schemas` selection.

Prerequisites:

- The destination database schema must already match the source. Run `db migrate latest --environment <to>` first.
- The destination connection user must be allowed to set `session_replication_role`. The default `postgres` superuser satisfies this.

## Configuration

The tool uses `.mvdmio-migrations.yml`.

```yaml
project: src/MyApp.Data
migrationsDirectory: Migrations
schemasDirectory: Schemas
schemas:
  - billing
  - identity
connectionStrings:
  local: Host=localhost;Database=mydb;Username=postgres;Password=secret
  acc: Host=acc-server;Database=mydb;Username=postgres;Password=secret
  prod: Host=prod-server;Database=mydb;Username=postgres;Password=secret
```

If a selected schema has foreign keys to tables in excluded schemas, the tool prints a warning. The export still succeeds, but replaying it into an empty database may require those referenced schemas and tables to already exist.

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
