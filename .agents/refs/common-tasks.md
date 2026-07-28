# Common Tasks

## Add a database migration

1. Create a class implementing `IDbMigration`.
2. Name it `_{identifier}_{name}` where identifier is a `YYYYMMDDHHmm` timestamp, e.g. `_202602161430_AddUsersTable`. `Identifier` and `Name` default from the class name (a Roslyn analyzer warns if the convention is broken); override them only when needed.
3. Implement `UpAsync(DatabaseConnection)` with the schema/data change.
4. Place it in the consuming assembly the `DatabaseMigrator` is configured to scan.

The `db` CLI tool can scaffold a migration for you (see below).

## Use the `db` CLI tool

The tool (`mvdmio.Database.PgSQL.Tool`, command `db`) handles: init config, scaffold migrations, run migrations (latest / to a target), pull schema, clean up obsolete migration files, and copy data. Configuration lives in a YAML file created by `db init`. Prefer the tool's commands over hand-editing for these workflows.

## Modify the migration framework

1. Core logic lives in `src/mvdmio.Database.PgSQL/Migrations/`. `DatabaseMigrator` orchestrates; favor extracting pure decision logic into separately unit-testable types.
2. The `mvdmio.migrations` tracking table is created/managed inside the migrator. Schema-table changes must be idempotent and run under the existing advisory lock (ADR 0001).
3. Add unit tests for pure logic and integration tests (with `TestBase`, and `SecondarySchema` when multiple assemblies are involved) for end-to-end behavior.
4. Respect `CONTEXT.md` vocabulary (Scope, Identifier, Watermark) and check `docs/adr/` before changing established behavior.

## Bump the package version

1. Edit `<PgSqlVersion>` in `Directory.Build.props` — this drives both the library and the tool.
2. Choose the bump by semver: MAJOR for an incompatible API change, MINOR for a backward-compatible feature, PATCH for a fix.
3. Update `README.md` to reflect the change.

## Add a tool command

1. Add a command class under `src/mvdmio.Database.PgSQL.Tool/Commands/` and wire it into `Program.cs` (uses `System.CommandLine`).
2. Put the command's logic in a handler/service alongside the existing ones (e.g. `Migrations/`, `Pull/`, `Copy/`) so it can be tested independently.
3. Add tests and update the tool's `README.md`.

## Before finishing any change

- Run `dotnet format` → `dotnet build` → `dotnet test` (sequentially), fix all errors and failures.
- Update `README.md` to reflect the latest state.
- Bump `PgSqlVersion` if the library or tool changed.
