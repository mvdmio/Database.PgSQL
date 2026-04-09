---
name: pgsql-tool-cli
description: Use the mvdmio.Database.PgSQL.Tool db CLI to initialize config, scaffold migrations, run migrations, pull schemas, and clean up obsolete migration files in this repository
compatibility: claude-code, opencode
---

# mvdmio.Database.PgSQL.Tool

Use this skill when working with the `mvdmio.Database.PgSQL.Tool` .NET CLI.

## Purpose

This tool manages PostgreSQL migrations and schema snapshots for projects using `mvdmio.Database.PgSQL`.

CLI command name after install: `db`

Repo-local invocation without installing the tool:

```bash
dotnet run --project src/mvdmio.Database.PgSQL.Tool/mvdmio.Database.PgSQL.Tool.csproj -- <command>
```

Examples:

```bash
dotnet run --project src/mvdmio.Database.PgSQL.Tool/mvdmio.Database.PgSQL.Tool.csproj -- init
dotnet run --project src/mvdmio.Database.PgSQL.Tool/mvdmio.Database.PgSQL.Tool.csproj -- migration create AddUsersTable
dotnet run --project src/mvdmio.Database.PgSQL.Tool/mvdmio.Database.PgSQL.Tool.csproj -- migrate latest -e local
dotnet run --project src/mvdmio.Database.PgSQL.Tool/mvdmio.Database.PgSQL.Tool.csproj -- pull -e prod
```

## Commands

### `db init`

Creates `.mvdmio-migrations.yml` in the current directory.

Default values:

```yaml
project: .
migrationsDirectory: Migrations
schemasDirectory: Schemas
connectionStrings:
  local: Host=localhost;Database=mydb;Username=postgres;Password=secret
```

### `db migration create <Name>`

Scaffolds a migration file in `migrationsDirectory`.

Behavior:
- Creates the directory if needed.
- Uses a UTC timestamp identifier in `yyyyMMddHHmm` format.
- Generates file name `_YYYYMMDDHHmm_Name.cs`.
- Resolves the namespace from the nearest project and the migration directory path.

Example:

```bash
db migration create AddUsersTable
```

### `db migrate latest`

Builds the configured project, loads migrations from the built assembly, and applies pending migrations.

### `db migrate to <identifier>`

Builds the configured project, loads migrations, and migrates up to the specified identifier inclusive.

Common options for both migrate commands:

```bash
db migrate latest --environment prod
db migrate latest -e local
db migrate latest --connection-string "Host=localhost;Database=mydb;Username=postgres;Password=secret"
db migrate to 202602161430 -e acc
```

Important behavior:
- The configured project is built during migrate.
- Migrations are tracked in `"mvdmio"."migrations"`.
- On an empty database, an embedded schema may be applied first if a matching schema file exists.

### `db pull`

Pulls the current database schema into `schemasDirectory`.

Output file naming:
- `schema.<environment>.sql` when `--environment` or default environment is used
- `schema.sql` when `--connection-string` is used directly

Does not generate table definition classes or `.mvdmio-translations.snapshot.json`.

Example:

```bash
db pull
db pull -e prod
db pull --connection-string "Host=localhost;Database=mydb;Username=postgres;Password=secret"
```

### `db cleanup`

For every configured environment:
- pulls a fresh schema file
- reads the migration version from the schema header
- finds the lowest migration version still needed anywhere
- deletes migration source files older than that version

Cleanup is skipped if any environment has no recorded migration version.

## Config Resolution

The tool searches upward from the current working directory for `.mvdmio-migrations.yml`.

Relative paths in the config are resolved from the directory containing that file.

Connection string resolution order:
1. `--connection-string`
2. `--environment` / `-e`
3. first `connectionStrings` entry

If an environment name is invalid, the tool prints the available environments.

## Agent Workflow

When asked to use this CLI, prefer this sequence:

1. Find or create `.mvdmio-migrations.yml`.
2. Confirm the target environment or explicit connection string.
3. Run the smallest command that solves the task.
4. Inspect generated files after `migration create`, `pull`, or `cleanup`.
5. Run `dotnet build` and `dotnet test` after making repo changes.

## Typical Tasks

Create a migration:

```bash
db migration create AddOrdersTable
```

Apply pending migrations locally:

```bash
db migrate latest -e local
```

Apply up to a specific migration:

```bash
db migrate to 202602161430 -e acc
```

Refresh a schema snapshot:

```bash
db pull -e prod
```

Remove obsolete migration source files:

```bash
db cleanup
```

## Expected File Effects

- `db init`: writes `.mvdmio-migrations.yml`
- `db migration create`: writes a new migration `.cs` file
- `db pull`: writes schema `.sql` file only
- `db cleanup`: may overwrite schema `.sql` files and delete old migration `.cs` files

## Guardrails

- Do not assume the globally installed `db` version matches the repo source; prefer `dotnet run --project ... -- <command>` when validating changes in this repository.
- `db migrate ...` builds the configured project as part of the command; build failures block migration.
- `db cleanup` is destructive for old migration source files. Review deleted files before committing.
- When using `--connection-string`, avoid leaking secrets into logs, docs, or commits.
