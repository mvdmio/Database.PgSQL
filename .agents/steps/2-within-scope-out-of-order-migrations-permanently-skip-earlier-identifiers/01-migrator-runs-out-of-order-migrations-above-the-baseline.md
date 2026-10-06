# 01 — Migrator runs out-of-order migrations above the Baseline

Status: pending
Depends on: none

## What to build

`DatabaseMigrator` stops skipping an **Out-of-order migration** for good. After this step, a migration from a branch that merged after a later-numbered migration in the same **Scope** had already run is picked up at the next `MigrateDatabaseToLatestAsync` / `MigrateDatabaseToAsync`, runs in **Identifier** order with every other pending migration, and is preceded by one logged warning. Schema-first bootstrap, hand-inserted cutoff rows and new Scopes behave exactly as before.

The pending rule (Spec, "Pending rule"; ADR-0002 already describes it):

- A Scope's **Baseline** is the lowest Identifier among rows that carry that Scope; its **Watermark** is the highest.
- A discovered migration is pending when no row carries its exact `(scope, identifier)` pair, **and** either its Scope has no rows or its Identifier is above the Baseline, **and**, when a target is given, its Identifier is at or below the target (global, inclusive ceiling).
- A pending migration is out-of-order when its Scope has rows and its Identifier is below the Watermark.
- Rows with no Scope belong to no Scope: they count toward no Baseline, no Watermark and no membership check.
- Pending migrations come back ordered by Identifier across all Scopes.

`PendingMigrationSelector` is the one place this rule lives; steps 02 and 03 call it from the `db` tool. The Spec says the selector "reports which pending migrations are out-of-order" but leaves the shape open. Reading used by every later step: `SelectPending` keeps its parameters and returns, per pending migration, the migration itself, whether it is out-of-order, and its Scope's Watermark (none when the Scope has no rows) — for example

```csharp
internal sealed record PendingMigration(IDbMigration Migration, bool IsOutOfOrder, long? ScopeWatermark);
```

Before running each out-of-order migration, `DatabaseMigrator` logs one warning through its existing `ILogger<DatabaseMigrator>` naming the migration's Scope, Identifier and name, and the Scope's Watermark. Nothing else is newly logged. An out-of-order migration runs through the same `RunAsync` path as any other (own transaction with its row insert); a failure raises `MigrationException` and stops the run, leaving earlier migrations of that run committed. No public API, constructor, table column or index changes.

Docs riding this step:

- Library README (`src/mvdmio.Database.PgSQL/README.md`), Migrations section: the Baseline rule in place of "ahead of the highest executed identifier", out-of-order migrations and the warning, how to keep one from running (delete its file or insert its row by hand), the `MigrateDatabaseToAsync` global ceiling applying to out-of-order migrations too, the accepted gap (below the first row of a never-bootstrapped Scope stays skipped), and a 0.40.0 upgrade note saying the first migrate runs every old out-of-order migration it finds, with a warning each, so operators check their databases first. Fix the "no watermark" wording in the scope-rename callout and the "Databases Without Scope Information" section to match.
- Root `README.md`: keep the one-line migrations summary accurate; adjust only if it no longer is.
- XML docs on `DatabaseMigrator` and `PendingMigrationSelector` describe the Baseline rule instead of the plain Watermark rule.
- `<PgSqlVersion>` goes from 0.39.1 to 0.40.0. Steps 02 and 03 ship in the same release and do not bump it again.

`CONTEXT.md` and ADR-0002 are already written; read them, do not rewrite them.

## Footprint

Projects: mvdmio.Database.PgSQL, mvdmio.Database.PgSQL.Tool, mvdmio.Database.PgSQL.Tests.Unit, mvdmio.Database.PgSQL.Tests.Integration (Docker must be running)

- `src/mvdmio.Database.PgSQL/Migrations/PendingMigrationSelector.cs` — `PendingMigrationSelector.SelectPending`, new pending-result type
- `src/mvdmio.Database.PgSQL/Migrations/DatabaseMigrator.cs` — `MigrateAsync` (selection loop, warning before out-of-order runs), class XML summary, `BackfillScopesAsync` warning text ("excluded from every scope's watermark")
- `test/mvdmio.Database.PgSQL.Tests.Unit/Migrations/PendingMigrationSelectorTests.cs` — existing tests adapted to the new return shape; new cases
- `test/mvdmio.Database.PgSQL.Tests.Integration/Migrations/PerScopeMigrationTests.cs` — `FixedMigrationSet`, `CreateTableMigration`, `MigrateDatabaseToLatestAsync_WithUnattributableLegacyRow_WarnsAndDoesNotSuppressOtherScopes` (logged-warning prior art)
- `test/mvdmio.Database.PgSQL.Tests.Integration/Migrations/SchemaFirstMigrationTests.cs` — `MigrateDatabaseToLatestAsync_WithPartialMigrationHistory_SkipsOlderMissingMigrationsAndRunsNewerOnes` (must pass unchanged), bootstrapped-scope prior art
- `test/mvdmio.Database.PgSQL.Tests.Integration/Fixture/CapturingLoggerFactory.cs` — `CapturingLoggerFactory.Entries`
- `src/mvdmio.Database.PgSQL/README.md` — `## Migrations`, `### Running Migrations`, `### Migration Scopes`, `### Databases Without Scope Information`
- `README.md` — "What The Library Gives You" migrations bullet
- `Directory.Build.props` — `<PgSqlVersion>`
- `CONTEXT.md`, `docs/adr/0002-per-scope-pending-migrations.md` — read only

## Acceptance criteria

- [ ] Selector unit test replaying Issue #2: rows `202610061205` and `202610061300` in Scope `mvdmio.Compliance.Db`, discovered `202610061047` in that Scope → `202610061047` is selected and flagged out-of-order.
- [ ] `SelectPending_WithAllMigrationsBelowScopeWatermark_RunsNone` keeps its expectation (nothing selected); only its name may change to say Baseline.
- [ ] A Scope with no rows still runs everything in Identifier order, none flagged out-of-order.
- [ ] Rows with no Scope count toward no Baseline: a scope-less row never hides or unhides a migration.
- [ ] A row with the same Identifier in another Scope does not make a migration present.
- [ ] Target ceiling below, at and above an out-of-order migration: excluded below, included at and above.
- [ ] Ordering across Scopes with an out-of-order migration in one of them follows Identifier order.
- [ ] Accepted-gap test: in a Scope with no bootstrap row, a migration below the Scope's first row stays unselected.
- [ ] Integration: an out-of-order migration runs late on a real database — its table exists and its `(scope, identifier)` row is recorded.
- [ ] Integration: that run logs exactly one warning naming the migration's Scope, Identifier, name and the Scope's Watermark; a run with no out-of-order migration logs no such warning.
- [ ] Integration: a bootstrapped Scope with folded migration files (below its baseline row) present does not run them.
- [ ] Integration: a failing out-of-order migration raises `MigrationException`, the migrations before it in that run stay recorded, and later ones do not run.
- [ ] `MigrateDatabaseToLatestAsync_WithPartialMigrationHistory_SkipsOlderMissingMigrationsAndRunsNewerOnes` and every other existing migration test pass unchanged in expectation.
- [ ] No change to `IDatabaseMigrator`, `DatabaseMigrator` constructors, `ExecutedMigrationModel`, `IDbMigration`, `RunAsync`, or the `mvdmio.migrations` columns and index.
- [ ] Library README covers the Baseline rule, out-of-order migrations, the warning, keeping a migration from running, the target ceiling, the accepted gap and the 0.40.0 upgrade note; root README summary is accurate.
- [ ] `<PgSqlVersion>` is 0.40.0.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` on the footprint projects pass, run sequentially.
