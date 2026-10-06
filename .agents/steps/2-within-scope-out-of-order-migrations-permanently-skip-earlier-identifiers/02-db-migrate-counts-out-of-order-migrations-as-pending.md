# 02 — `db migrate` counts out-of-order migrations as pending

Status: pending
Depends on: 01

## What to build

`db migrate latest` and `db migrate to <identifier>` decide "already up to date" with the library's `PendingMigrationSelector` from step 01 instead of the tool's own copy of the plain Watermark rule. With only an **Out-of-order migration** pending, the tool no longer prints "Database is already up to date" (or "... for the specified target") and stops; it goes on to call the migrator, which runs the migration and logs its warning. The tool's `ConsoleMigratorLogger` already prints warnings, so the console shows `Warning: ...` for each out-of-order migration with no extra code.

`CountPendingMigrations` and its Watermark logic go away; the pending count is the number of migrations the selector returns for the rows read and, for `db migrate to`, the request's target. The rows the tool reads are not backfilled, so scope-less legacy rows count toward nothing — the same as today, and the migrator's own run still backfills before it selects. The "Found N migration(s), M already applied" and "Migration complete" lines and the schema-first reporting path stay as they are.

Docs riding this step, in the tool README (`src/mvdmio.Database.PgSQL.Tool/README.md`):

- `db migrate latest` / `db migrate to`: pending includes out-of-order migrations, and the command prints a warning naming each one it runs.
- `db pull`: migrate a database before pulling its schema — a schema pulled from a database that has not yet run an out-of-order migration folds that migration in without its effect (the header records only each Scope's Watermark), and a database bootstrapped from it never runs it.

Version stays 0.40.0 (bumped in step 01).

## Footprint

Projects: mvdmio.Database.PgSQL.Tool, mvdmio.Database.PgSQL.Tests.Unit, mvdmio.Database.PgSQL.Tests.Integration (references the Tool)

- `src/mvdmio.Database.PgSQL.Tool/Migrations/MigrationExecutionService.cs` — `ExecuteAsync`, `CountPendingMigrations` (removed), `GetTargetMigrations`
- `src/mvdmio.Database.PgSQL/Migrations/PendingMigrationSelector.cs` — `SelectPending` (called, not changed)
- `test/mvdmio.Database.PgSQL.Tests.Unit/Migrations/MigrationExecutionServiceTests.cs` — `FakeMigrationRuntime`, `FakeDbMigration`, `ExecuteAsync_LatestWithScopeWatermarkCoveringAllMigrations_ReportsUpToDateAndSkipsMigrator`, `ExecuteAsync_LatestWithSameIdentifierExecutedInAnotherScope_StillRunsMigrator`
- `src/mvdmio.Database.PgSQL.Tool/README.md` — `### db migrate latest`, `### db migrate to <identifier>`, `### db pull`
- `.claude/skills/pgsql-tool-cli/SKILL.md` — `db migrate latest` "Important behavior" list (add that out-of-order migrations run with a warning)

## Acceptance criteria

- [ ] Unit test: with rows whose Watermark sits above a discovered migration that has no row and sits above the Baseline (only an out-of-order migration pending), `db migrate latest` does not report "Database is already up to date" and calls `MigrateDatabaseToLatestAsync` once.
- [ ] The same holds for `db migrate to` with a target at or above that migration; with a target below it, the tool reports up to date for the target and does not call the migrator.
- [ ] Existing `MigrationExecutionServiceTests` keep their expectations — a bootstrap-style row above every migration still reports up to date; a same-Identifier row in another Scope still runs the migrator.
- [ ] The tool holds no copy of the pending rule; it calls `PendingMigrationSelector`.
- [ ] Tool README describes the `db migrate` warning and "migrate a database before pulling its schema"; the `pgsql-tool-cli` skill notes the out-of-order behaviour.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` on the footprint projects pass, run sequentially.
