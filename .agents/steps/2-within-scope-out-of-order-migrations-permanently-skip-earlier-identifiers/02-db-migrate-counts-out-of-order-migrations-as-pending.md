# 02 — `db migrate` counts out-of-order migrations as pending

Status: built
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

- [x] Unit test: with rows whose Watermark sits above a discovered migration that has no row and sits above the Baseline (only an out-of-order migration pending), `db migrate latest` does not report "Database is already up to date" and calls `MigrateDatabaseToLatestAsync` once.
- [x] The same holds for `db migrate to` with a target at or above that migration; with a target below it, the tool reports up to date for the target and does not call the migrator.
- [x] Existing `MigrationExecutionServiceTests` keep their expectations — a bootstrap-style row above every migration still reports up to date; a same-Identifier row in another Scope still runs the migrator.
- [x] The tool holds no copy of the pending rule; it calls `PendingMigrationSelector`.
- [x] Tool README describes the `db migrate` warning and "migrate a database before pulling its schema"; the `pgsql-tool-cli` skill notes the out-of-order behaviour.
- [x] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` on the footprint projects pass, run sequentially.

## Outcome

- `MigrationExecutionService.ExecuteAsync` now counts pending migrations with `PendingMigrationSelector.SelectPending(alreadyExecuted, targetMigrations, request.TargetIdentifier).Count`. `CountPendingMigrations` is gone. `GetTargetMigrations` is unchanged. The doc comments on `TryReportSchemaPathAsync` ("no rows yet") and `ConsoleMigratorLogger` (names out-of-order warnings) were updated to match.
- New unit tests in `MigrationExecutionServiceTests`: `ExecuteAsync_LatestWithOnlyOutOfOrderMigrationPending_RunsMigrator`, `ExecuteAsync_TargetAtOrAboveOutOfOrderMigration_RunsMigratorToTarget` (targets 202602161500 and 202602161600) and `ExecuteAsync_TargetBelowOutOfOrderMigration_ReportsUpToDateAndSkipsMigrator`. The existing test `ExecuteAsync_LatestWithScopeWatermarkCoveringAllMigrations_ReportsUpToDateAndSkipsMigrator` is renamed to `...ScopeBaselineCoveringAllMigrations...`. Its expectation is unchanged. Two test comments no longer mention the Watermark rule or `CountPendingMigrations`.
- The "Found N migration(s), M already applied" line still prints the raw row count, as the Step asks. With out-of-order rows it can read "2 migration(s), 2 already applied" and still go on to apply 2.
- Tool README: `db migrate latest` describes the Baseline rule, out-of-order migrations and their `Warning:` line. `db migrate to` describes the target ceiling. `db pull` has a "Migrate a database before pulling its schema" paragraph. Two "no watermark yet" phrases now read "no rows yet". `pgsql-tool-cli` skill: one bullet added to the `db migrate latest` "Important behavior" list.
- Tests ran: the full unit project (225 passed) with `DOTNET_ROLL_FORWARD=Major`. The integration project was built but not run. It has no tests for the Tool's `MigrationExecutionService`.
- Run recipe: none exists. The rung-4 drive is done by hand with the commands that `02-db-migrate-out-of-order.sh` in the Proof folder lists. That script cannot run in this sandbox as a whole, so each command was run on its own. It uses its own `postgres:16` container on port 55432, and the SecondarySchema fixture project serves as the migrations project.

Safety fact: `db migrate` counts a migration with no row above its Scope's Baseline but below its Watermark as pending and goes on to run the migrator; if false, the tool prints "Database is already up to date" and the out-of-order migration never reaches the migrator, so it is skipped silently (rung 4)
Proof: `DOTNET_ROLL_FORWARD=Major dotnet run --project src/mvdmio.Database.PgSQL.Tool --framework net10.0 -- migrate latest -e proof` (seeded rows 202505180000 and 202505200000; config `02-mvdmio-migrations.yml`, seed `02-seed.sql`) exit 0 — `Warning: Running out-of-order migration 202505181100 (SecondaryTable) ...`, `Migration complete. 2 migration(s) applied.`, tables `secondary_table` and `secondary_follow_up_table` exist; transcript in `/data/projects/mvdmio/Database.PgSQL/.git/proof/2-within-scope-out-of-order-migrations-permanently-skip-earlier-identifiers/02-db-migrate-out-of-order.txt`. Unit: `DOTNET_ROLL_FORWARD=Major dotnet test test/mvdmio.Database.PgSQL.Tests.Unit/mvdmio.Database.PgSQL.Tests.Unit.csproj --filter FullyQualifiedName~MigrationExecutionServiceTests` exit 0, 14 passed (`02-migrate-execution.txt`)
