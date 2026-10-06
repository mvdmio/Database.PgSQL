# Within-scope out-of-order migrations permanently skip earlier identifiers

## Problem Statement

Teams that use `mvdmio.Database.PgSQL` migrations work on parallel branches. Each branch adds migrations named by a `YYYYMMDDHHmm` timestamp. A branch merged later can carry a migration with an earlier timestamp than one already applied from another branch, in the same Scope.

Today the migrator runs a migration only when its Identifier is above the highest Identifier recorded for its Scope (the Watermark). The earlier-numbered migration therefore never runs. Nothing is logged, and nothing reports it as missing. The database silently lacks its change until someone notices that the application fails.

This happened in mvdmio-suite on 2026-10-06. `_202610061047_VoiceCallReplacesMeetingSession` in Scope `mvdmio.Compliance.Db` was skipped on the shared dev database, because `202610061205` and `202610061300` from another branch had already run. Compliance could not start until someone renamed the migration so that it sorted above the Watermark. The suite has worked around the same problem three times before: a 20-migration Roles catch-up, a Tickets catch-up, and a Statistics schema catch-up. Each one was a hand-written migration that replays the skipped work and inserts its rows into `mvdmio.migrations`. The suite's own guidance now tells people to write such catch-ups. It also forbids renumbering migrations, which the last workaround had to break.

## Solution

The migrator runs every migration whose own row is missing, as long as the migration sits above its Scope's **Baseline**. The Baseline is the lowest Identifier recorded for that Scope. Everything at or below the Baseline still counts as present without a row of its own. That keeps schema-first bootstrap working, because a bootstrap records one row per Scope to stand for every migration folded into `schema.sql`.

A migration with no row that sits above the Baseline but below the Watermark is an **Out-of-order migration**. It runs late, at the next migrate, in Identifier order with the other pending migrations, and the migrator logs a warning naming it. If it fails, the run stops with a `MigrationException`, as for any other migration.

The `db` tool follows the same rule. `db migrate` counts out-of-order migrations as pending and prints their warnings. `db cleanup` never deletes a migration file that is still pending in any configured environment.

Nobody needs to renumber a migration or write a catch-up by hand again.

## User Stories

1. As an application developer, I want a migration from my branch to run even when a later-numbered migration from another branch already ran in the same Scope, so that merge order does not decide whether my schema change reaches the database.
2. As an application developer, I want an out-of-order migration to run at the next migrate without any action from me, so that I do not have to renumber it.
3. As an application developer, I want never to write a catch-up migration that replays skipped migrations and inserts their rows by hand, so that the migrations folder holds only real schema changes.
4. As an application developer, I want the migrator to log a warning naming each out-of-order migration it runs (Scope, Identifier, name), so that I can see that a migration ran later than its number suggests.
5. As an application developer, I want an out-of-order migration that conflicts with the current schema to stop the run with a `MigrationException` naming it, so that a conflict is loud and never silent.
6. As an application developer, I want the migrations that ran before a failed out-of-order migration to stay committed, so that a failure behaves exactly as it does for any other migration today.
7. As an application developer, I want out-of-order migrations to run in Identifier order together with every other pending migration across Scopes, so that the run order stays predictable and matches today's ordering.
8. As an application developer, I want an out-of-order migration to run before any newer pending migration, so that the run follows Identifier order as far as the database still allows.
9. As an application developer using schema-first bootstrap, I want migrations folded into my `schema.sql` never to run again, so that bootstrap keeps working after this change.
10. As an application developer, I want migration files that are folded into `schema.sql` but not yet removed by `db cleanup` to stay ignored, so that keeping old files around stays harmless.
11. As an application developer who adopted an existing database by inserting a cutoff row, I want migrations below that row to stay skipped, so that the adopted database is not migrated a second time.
12. As an application developer, I want a Scope with no rows yet to run all of its migrations, so that a new Scope starts from its first migration as it does today.
13. As an application developer sharing one database with other applications, I want each Scope's Baseline and Watermark to depend only on that Scope's rows, so that another application's migrations never change what runs in mine.
14. As an application developer with legacy rows that carry no Scope, I want those rows to count toward no Scope's Baseline, so that unattributed legacy rows cannot hide or unhide my migrations.
15. As an application developer calling `MigrateDatabaseToAsync(target)`, I want an out-of-order migration at or below the target to run and one above the target to wait, so that the global ceiling keeps its meaning.
16. As an application developer, I want to keep a specific out-of-order migration from running by deleting its file or inserting its row by hand, so that I keep control without a new API.
17. As an operator upgrading an existing database to 0.40.0, I want the first migrate to run every out-of-order migration it finds and warn about each one, so that long-skipped changes finally reach the database.
18. As an operator, I want the README's upgrade note to say that the first migrate on 0.40.0 runs old out-of-order migrations, so that I can check my databases before I deploy.
19. As a developer on a shared dev database, I want another branch's out-of-order migration to apply when that branch's code runs, so that the shared database does not block anyone's startup.
20. As a `db` tool user, I want `db migrate` to count out-of-order migrations as pending, so that it does not print "Database is already up to date" and stop before migrating while an out-of-order migration still waits.
21. As a `db` tool user, I want `db migrate` to print the warning for each out-of-order migration, so that I see it in the console as well as in application logs.
22. As a `db` tool user, I want `db cleanup` never to delete a migration file that is still pending in any configured environment, so that cleanup cannot turn a late migration into a lost one.
23. As a `db` tool user, I want `db cleanup` to keep deleting migration files that every environment already has, so that cleanup still shrinks the migrations folder.
24. As a `db` tool user, I want the README to tell me to migrate a database before pulling its schema, so that the pulled `schema.sql` does not fold in an out-of-order migration without its effect.
25. As a library maintainer, I want the pending rule to live in one place that both the migrator and the `db` tool use, so that the two can never disagree about what is pending.
26. As a library maintainer, I want no new column on `mvdmio.migrations` and no public API change, so that the upgrade needs no table migration and no consumer code change.
27. As a library maintainer, I want ADR-0002 and `CONTEXT.md` to describe the Baseline rule, so that nobody restores the plain Watermark rule later.
28. As a library maintainer, I want the release versioned as 0.40.0, so that the behaviour change follows the 0.x precedent of shipping breaking changes as MINOR bumps.

## Implementation Decisions

- **Pending rule.** The internal pending-migration selector decides pending migrations per Scope from the rows recorded for that Scope:
  - The **Baseline** of a Scope is the lowest recorded Identifier among rows that carry that Scope.
  - The **Watermark** of a Scope is the highest recorded Identifier among those rows.
  - A discovered migration is pending when no row carries its exact `(scope, identifier)` pair, **and** either its Scope has no rows or its Identifier is above the Baseline, **and**, when a target is given, its Identifier is at or below the target.
  - A pending migration is **out-of-order** when its Scope has rows and its Identifier is below the Watermark.
  - Rows with no Scope belong to no Scope. They count toward no Baseline, no Watermark and no membership check, as they count toward no Watermark today. The backfill runs before selection, so the only rows still without a Scope at that point are ones it could not attribute. That includes a row whose Identifier two discovered Scopes share. Such a row's migration is pending again when it sits above its Scope's Baseline. If it already ran, it fails loudly with `MigrationException` rather than being skipped. That matches ADR-0002: Identifiers do not collide across Scopes in practice, and a collision should fail loudly. The fix is to set that row's Scope by hand.
  - Pending migrations are ordered by Identifier across all Scopes, as today.
- **One rule, two callers.** The selector reports which pending migrations are out-of-order. `DatabaseMigrator` and the `db` tool both use it. The `db` tool's own copy of the Watermark rule in its pending count goes away. Today that count only decides whether `db migrate` reports "already up to date" and stops before calling the migrator, so an out-of-order migration would otherwise never reach the migrator from the tool. The Tool already sees the library's internals through `InternalsVisibleTo`.
- **Warning.** Before running each out-of-order migration, `DatabaseMigrator` logs one warning through its existing `ILogger<DatabaseMigrator>`. The warning names the migration's Scope, Identifier and name, and the Scope's Watermark. Nothing else is newly logged. The `db` tool's console logger already prints warnings, so `db migrate` shows these with no extra work.
- **Failure.** An out-of-order migration runs exactly like any other: in its own transaction, together with its row insert. A failure raises `MigrationException` and stops the run. Earlier migrations in that run stay committed.
- **Target ceiling.** `MigrateDatabaseToAsync(target)` keeps the global, inclusive ceiling. It applies to out-of-order migrations the same way it applies to new ones.
- **No schema change.** `mvdmio.migrations` keeps its columns and its `(scope, identifier)` unique index. No column marks baseline rows. The Baseline is derived from the rows that already exist, so rows written by every earlier version keep their meaning.
- **No public API change.** `IDatabaseMigrator`, `DatabaseMigrator`'s constructors, `ExecutedMigrationModel`, `IDbMigration` and `RunAsync` are unchanged. There is no setting, no dry-run, and no "mark as applied" API. A row that `RunAsync` records below a Scope's Baseline becomes that Scope's new Baseline; this follows from the definition and needs no special handling.
- **Schema-first bootstrap unchanged.** Bootstrap still applies an assembly's embedded schema when at least one Scope it vouches for has no rows, and still records one baseline row per vouched Scope. That row becomes the Scope's Baseline, so folded migrations stay below it.
- **Advisory lock unchanged.** Selection and the whole run, out-of-order migrations included, stay under the existing session-scoped advisory lock.
- **`db cleanup`.** Today cleanup only pulls each environment's schema and reads its header lines; it neither loads the migrations project nor reads `mvdmio.migrations`. Both are new work. Before it deletes a migration file, cleanup checks every configured environment's `mvdmio.migrations` rows against the project's discovered migrations with the library's selector. It keeps any file whose migration is pending in at least one environment. A file name carries an Identifier but no Scope, so cleanup matches a file to every discovered migration with that Identifier. It keeps the file when any of those migrations is pending in any environment. When in doubt, cleanup keeps the file. Its existing deletion bound (the lowest header line across Scopes and environments) and its other skip rules stay as they are. The pending check only removes files from the delete list.
- **`db pull`.** Unchanged. Its header still records each Scope's Watermark. The limitation is documented, not fixed: a schema pulled from a database that has not run an out-of-order migration folds that migration in without its effect.
- **Docs.**
  - The library README's migrations section describes the Baseline rule, out-of-order migrations, the warning, how to keep a migration from running, the `MigrateDatabaseToAsync` ceiling, and an upgrade note for 0.40.0.
  - The tool README describes the `db migrate` warning, the new `db cleanup` rule, and "migrate a database before pulling its schema".
  - The root README's one-line migration summary stays accurate, or gets adjusted to match.
- **Domain docs (already written in this session).**
  - `CONTEXT.md` defines **Baseline** and **Out-of-order migration**, and has rewritten **Scope** and **Watermark** entries.
  - ADR-0002 is rewritten in place as "Decide pending migrations per scope by recorded rows above the baseline" (`docs/adr/0002-per-scope-pending-migrations.md`).
- **Version.** `<PgSqlVersion>` goes from 0.39.1 to 0.40.0. While the package is on 0.x, behaviour-breaking changes ship as MINOR bumps; the per-scope watermark change went from 0.27.0 to 0.28.0.

## Testing Decisions

- A good test checks external behaviour. It looks at which migrations the selector returns and flags as out-of-order, which tables and rows exist after a real run, what the migrator logs, and which files cleanup deletes. It does not check how the selector computes those answers.
- **Selector unit tests** extend the existing pending-migration selector tests:
  - Replay of Issue #2: rows `202610061205` and `202610061300` in Scope `mvdmio.Compliance.Db`, and a discovered `202610061047`. The test expects `202610061047` to be selected and flagged out-of-order.
  - Folded migrations below a bootstrap row stay unselected. The existing `SelectPending_WithAllMigrationsBelowScopeWatermark_RunsNone` keeps its expectation; only its name may change to say Baseline.
  - A Scope with no rows runs everything in order. This is the existing test.
  - Rows with no Scope count toward no Baseline.
  - A target ceiling below, at and above an out-of-order migration.
  - Ordering across Scopes with an out-of-order migration in one of them.
  - The accepted gap: in a Scope with no bootstrap row, a migration below the Scope's first row stays unselected. This test documents the limitation.
- **Integration tests** (Testcontainers, so Docker must run) use the existing per-scope and schema-first migration test classes as prior art:
  - An out-of-order migration runs late on a real database. Its table exists afterwards, and its row is recorded.
  - The out-of-order run logs a warning naming the migration. The existing test that asserts the unattributable-legacy-row warning is the prior art for asserting a logged warning.
  - A bootstrapped Scope with folded migration files present does not run them.
  - `MigrateDatabaseToLatestAsync_WithPartialMigrationHistory_SkipsOlderMissingMigrationsAndRunsNewerOnes` keeps passing unchanged. Its only row, `202505181000`, belongs to the `SimpleTable` migration and becomes the Scope's Baseline; the two later fixture migrations sit above it and run, and no discovered migration sits between them without a row.
  - A failing out-of-order migration raises `MigrationException`, and the migrations before it stay recorded.
- **Tool unit tests** extend the existing cleanup planner tests and migration execution service tests:
  - Cleanup keeps a file that is pending in one environment and deletes a file that every environment has.
  - With only an out-of-order migration pending, `db migrate` does not report "already up to date" and goes on to run the migrator.

## Out of Scope

- A column or other marker that tells baseline rows apart from rows of migrations that actually ran.
- A setting to choose between running out-of-order migrations late and stopping with an error.
- A `--dry-run` option or a status command for `db migrate`.
- A "mark as applied" API.
- Recording existing gaps as applied on upgrade, instead of running them.
- Closing the accepted gap: a migration numbered below the first row of a Scope that was never bootstrapped stays skipped.
- Making the `schema.sql` header express gaps, so that a schema pulled before an out-of-order migration ran would still be correct.
- Making `db cleanup`'s deletion bound Scope-aware, beyond keeping pending files.
- Removing the `[Obsolete]` Scope backfill, which waits for the next major version.
- Special handling for legacy rows with no Scope whose Identifier two Scopes share. Their migration runs again and fails loudly, as described under Implementation Decisions.
- Updating mvdmio-suite's own migration guidance, its catch-up pattern and its "no renumber" rule. That is the suite's work once it moves to 0.40.0.

## Further Notes

- **Suite exposure on upgrade.** The suite's Roles catch-up (20 migrations) and Tickets catch-up both inserted rows for the migrations they replayed, so neither will cause a migration to run twice. The Statistics catch-up stands in for a skipped schema bootstrap, not for a migration, and records no row; no Statistics migration is left without a row in the code. About a third of the suite's migrations are plain, non-idempotent DDL. Any migration that ran by hand without a row would therefore fail loudly on upgrade, but the suite's code shows none.
- **Folded files in the suite.** About 59 migration files at or below their Scope's bootstrap row remain in the suite's Db projects. They stay below the Baseline and never run.
- **The 2026-10-06 rename.** Commit `0cf0fc508` in mvdmio-suite renamed `202610061047` to `202610062245`. Any environment where `202610061047` had already run keeps a row that has no file. Rows without a file are harmless under this rule.
