# 03 — `db cleanup` keeps migration files still pending in any environment

Status: pending
Depends on: 01, 02

## What to build

`db cleanup` can no longer turn a late migration into a lost one. Before it deletes a migration file, it checks every configured environment's `mvdmio.migrations` rows against the project's discovered migrations with the library's `PendingMigrationSelector` (step 01), and keeps any file whose migration is pending in at least one environment. Files every environment already has are still deleted as today.

Unchanged: cleanup still pulls and writes each environment's schema file, still derives its deletion bound from the lowest header line across Scopes and environments, and keeps its existing skip rules ("No environments configured.", "At least one environment has no recorded migration version."). The pending check only removes files from the delete list; it never adds one.

New work, because cleanup today neither loads the migrations project nor reads `mvdmio.migrations`:

- Cleanup builds and loads the configured migrations project the way `db migrate` does (`MigrationProjectLoader`, `ToolPathResolver.GetProjectPath`). Reading where the Spec is silent: it loads the project before touching any environment, so a build failure stops cleanup before any schema file is rewritten or any file deleted.
- For each environment it reads the recorded rows through the same runtime `db migrate` uses (`IMigrationRuntime`: `IsDatabaseEmptyAsync`, then `RetrieveAlreadyExecutedMigrationsAsync`). An environment with no migrations table, or an empty one, has no rows, so every discovered migration is pending there. Rows are not backfilled; scope-less rows count toward nothing, which can only keep more files.
- Matching: a file name carries an Identifier but no Scope, so a file matches every discovered migration with that Identifier, and cleanup keeps the file when any of them is pending in any environment (no target ceiling). Reading where the Spec is silent: a file whose Identifier matches no discovered migration has nothing the pending check can point to, so it keeps today's treatment (deleted when below the bound).
- The pending decision stays unit-testable in `MigrationCleanupPlanner`: the planner receives the discovered migrations and each environment's rows alongside the header bounds, and asks the selector.
- Reading where the Spec is silent: for each file kept only because it is pending, cleanup prints one line naming the file and the environment(s) where its migration is still pending.

Docs riding this step:

- Tool README `### db cleanup`: cleanup builds the project and reads each environment's migration rows, and never deletes a file still pending in any configured environment.
- Root `README.md` CLI table row for `db cleanup` stays accurate, or is adjusted to match.
- `.claude/skills/pgsql-tool-cli/SKILL.md` `### db cleanup`: add the build step and the pending rule to its list.

Version stays 0.40.0. This is the last step: it leaves the whole solution green.

## Footprint

Projects: mvdmio.Database.PgSQL, mvdmio.Database.PgSQL.Tool, mvdmio.Database.PgSQL.Analyzers, mvdmio.Database.PgSQL.Tests.Unit, mvdmio.Database.PgSQL.Tests.Integration, mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema, mvdmio.Database.PgSQL.Tests.Integration.OData, mvdmio.Database.PgSQL.Analyzers.Tests, mvdmio.Database.PgSQL.Tests.Packaging (whole solution; Docker, and network for Packaging)

- `src/mvdmio.Database.PgSQL.Tool/Cleanup/MigrationCleanupPlanner.cs` — `MigrationCleanupPlanner.Plan`, `MigrationCleanupPlan`, `TryParseMigrationIdentifier`
- `src/mvdmio.Database.PgSQL.Tool/Commands/CleanupCommand.cs` — `CleanupCommand.Create` action (project load, per-environment row read, kept-file output)
- `src/mvdmio.Database.PgSQL.Tool/Migrations/MigrationProjectLoader.cs` — `MigrationProjectLoader.Load`, `MigrationProjectContext`
- `src/mvdmio.Database.PgSQL.Tool/Migrations/MigrationExecutionService.cs` — `IMigrationRuntime`, `IMigrationRuntimeFactory`, `DatabaseMigrationRuntimeFactory` (reused to read rows)
- `src/mvdmio.Database.PgSQL.Tool/Configuration/ToolPathResolver.cs` — `GetProjectPath`, `GetMigrationsDirectoryPath`
- `src/mvdmio.Database.PgSQL/Migrations/PendingMigrationSelector.cs` — `SelectPending` (called, not changed)
- `test/mvdmio.Database.PgSQL.Tests.Unit/Cleanup/MigrationCleanupPlannerTests.cs` — existing tests adapted to the new planner inputs; new cases
- `src/mvdmio.Database.PgSQL.Tool/README.md` — `### db cleanup`
- `README.md` — "What The CLI Gives You" table, `db cleanup` row
- `.claude/skills/pgsql-tool-cli/SKILL.md` — `### db cleanup`

## Acceptance criteria

- [ ] Planner unit test: with two environments, a file below the bound whose migration is pending in one environment (for example an out-of-order migration there) is kept, and a file below the bound that every environment has is deleted.
- [ ] Planner unit test: a file whose Identifier two discovered Scopes share is kept when either of those migrations is pending in any environment.
- [ ] Planner unit test: an environment with no rows keeps every file that matches a discovered migration.
- [ ] Existing planner tests keep their skip reasons, bound and deletion results for files nothing marks pending.
- [ ] `db cleanup` loads the migrations project before rewriting any schema file, reads every configured environment's rows, and prints a line for each file kept because it is still pending.
- [ ] Tool README, root README CLI table and `pgsql-tool-cli` skill describe the new cleanup rule.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` for the whole solution pass, run sequentially.
