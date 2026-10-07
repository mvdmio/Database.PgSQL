# 03 — `db cleanup` keeps migration files still pending in any environment

Status: built
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

- [x] Planner unit test: with two environments, a file below the bound whose migration is pending in one environment (for example an out-of-order migration there) is kept, and a file below the bound that every environment has is deleted.
- [x] Planner unit test: a file whose Identifier two discovered Scopes share is kept when either of those migrations is pending in any environment.
- [x] Planner unit test: an environment with no rows keeps every file that matches a discovered migration.
- [x] Existing planner tests keep their skip reasons, bound and deletion results for files nothing marks pending.
- [x] `db cleanup` loads the migrations project before rewriting any schema file, reads every configured environment's rows, and prints a line for each file kept because it is still pending.
- [x] Tool README, root README CLI table and `pgsql-tool-cli` skill describe the new cleanup rule.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` for the whole solution pass, run sequentially.

## Outcome

- `MigrationCleanupPlanner.Plan(migrationsDirectoryPath, discoveredMigrations, environments)` now takes the discovered migrations and one `CleanupEnvironment(Name, LowestHeaderIdentifier, ExecutedMigrations)` per environment, in place of the list of header identifiers. It runs `PendingMigrationSelector.SelectPending` per environment with no target. It moves a file below the bound into `MigrationCleanupPlan.PendingFilesKept` (a new `PendingMigrationFile(Path, PendingEnvironments)` list) when a discovered migration with the file's identifier is pending in any environment. The skip reasons, the bound and `TryParseMigrationIdentifier` are unchanged. A file whose identifier matches no discovered migration is still deleted when below the bound.
- `CleanupCommand` loads the project with `MigrationProjectLoader` right after the "No environments configured" check, before the first schema pull. For each environment, after the pull, it reads rows through `DatabaseMigrationRuntimeFactory` (`IsDatabaseEmptyAsync`, then `RetrieveAlreadyExecutedMigrationsAsync`), with no backfill. It prints `Kept <file>: still pending in <environments>` for each kept file. When files were kept but none deleted, it prints "No migration files were deleted." in place of "No migration files are older than the lowest environment version." `MigrationProjectLoader`, `MigrationExecutionService` and `ToolPathResolver` were called, not changed.
- `CleanupCommand` has no unit-test seam. The rung-4 drive below covers its project load, row read and kept-file lines.
- Unit tests in `MigrationCleanupPlannerTests`: the four existing tests are moved to the new inputs and keep their expectations, and three new tests are added: `Plan_WithMigrationPendingInOneEnvironment_KeepsItAndDeletesFileEveryEnvironmentHas`, `Plan_WithIdentifierSharedByTwoScopes_KeepsFileWhenEitherMigrationIsPending` and `Plan_WithEnvironmentWithoutRows_KeepsEveryFileMatchingADiscoveredMigration`. The full unit project passed (228), run with `DOTNET_ROLL_FORWARD=Major`. `dotnet format --verify-no-changes` exited 0 and the whole solution built. The integration and packaging suites were not run; they are the Checker's.
- Docs: the tool README `### db cleanup` has a new paragraph and list on the pending rule. The root README `db cleanup` row now reads "Refresh schema files; delete migrations no environment still needs". The `pgsql-tool-cli` skill's `### db cleanup` adds the build step, the row read and the keep rule.
- This session cannot write to the Proof folder or run a proof script as a whole. The script, seeds and transcript are in this session's scratchpad, `/tmp/claude-1000/-data-projects-mvdmio-Database-PgSQL/8b337ed6-dfe2-45f4-a85a-ff7512540a43/scratchpad/proof03/` (`03-db-cleanup-keeps-pending.sh`, `03-seed-dev.sql`, `03-seed-prod.sql`, `03-db-cleanup-keeps-pending.txt`). Each command was run on its own, against a postgres:16 container on port 55433 that has since been removed.
- Run recipe: none exists, and none was written; the drive follows Step 02's hand-run pattern.

Safety fact: `db cleanup` keeps a migration file below the deletion bound when a migration with its identifier is still pending (for example out-of-order) in any configured environment; if false, cleanup deletes the file and the late migration is lost for good (rung 4)
Proof: `DOTNET_ROLL_FORWARD=Major dotnet run --project src/mvdmio.Database.PgSQL.Tool --framework net10.0 -- cleanup`, run against a fresh postgres:16 with database envdev holding every fixture row and envprod holding only 202505180000 and 202505200000 (steps in `03-db-cleanup-keeps-pending.sh`), exit 0 — `Kept .../_202505181100_SecondaryTable.cs: still pending in prod`, `Kept .../_202505190000_SecondaryFollowUp.cs: still pending in prod`, `Deleted .../_202505170000_Removed.cs`; transcript `/tmp/claude-1000/-data-projects-mvdmio-Database-PgSQL/8b337ed6-dfe2-45f4-a85a-ff7512540a43/scratchpad/proof03/03-db-cleanup-keeps-pending.txt` (belongs in the Proof folder, which this session cannot write)
