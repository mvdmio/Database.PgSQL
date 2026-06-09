# Protect the migration runner against concurrent instances with an advisory lock

## Problem Statement

When several instances of the same application start at once (rolling deploys, autoscaling, multi-pod startup), each builds a `DatabaseMigrator` and calls `MigrateDatabaseToLatestAsync` / `MigrateDatabaseToAsync` at nearly the same moment. Nothing coordinates them. As a result, multiple instances race through the migration orchestration: several can pass the empty-database check and all apply the embedded schema, or several can try to run and record the same migration at the same time. The symptoms are duplicate-key failures on the `mvdmio.migrations` table, partially-applied schema, and application instances booting against a half-migrated database. The developer wants migrations to "just work" safely no matter how many instances start together.

## Solution

Serialize the entire migration run across every instance using a single **session-scoped PostgreSQL advisory lock**. Before doing any work, the migrator acquires the lock; only one instance holds it at a time. Every other instance **blocks** until the holder finishes, then acquires the lock, re-reads the current migration state, finds nothing left to do, and continues. No instance proceeds to run its application until migrations are fully settled. The protection is always on, requires no configuration, and does not change any public API. From the developer's perspective, they call the same migration methods they always have — concurrency safety is now built in.

## User Stories

1. As an application operator, I want concurrent instances starting together to apply migrations exactly once, so that I never get duplicate-key errors on the migrations table.
2. As an application operator, I want only one instance to apply the embedded schema on an empty database, so that schema-first bootstrap is never applied twice.
3. As an application operator, I want instances that did not win the race to wait for the migrating instance to finish, so that no instance boots against a half-migrated database.
4. As an application operator, I want a waiting instance to re-check what has already been applied once it acquires the lock, so that it does not re-run migrations the winning instance already completed.
5. As an application operator, I want the waiting instance to block for as long as the migration genuinely takes, so that a long-but-healthy migration is never mistaken for a failure.
6. As an application operator, I want the lock to be released automatically if the migrating instance crashes, so that a dead instance can never permanently wedge every other instance.
7. As a developer, I want concurrency protection to be on by default with no flag to set, so that I cannot accidentally deploy without it.
8. As a developer, I want no change to the `DatabaseMigrator` constructors or `IDatabaseMigrator` interface, so that upgrading is a drop-in with no code changes.
9. As a developer, I want the protection to cover the empty-database check and schema application as well as the migration loop, so that the bootstrap path is as safe as the incremental path.
10. As a developer using `MigrateDatabaseToAsync` to migrate to a specific version, I want the same concurrency protection as `MigrateDatabaseToLatestAsync`, so that targeted migrations are equally safe.
11. As a developer who already opened the connection before calling the migrator, I want the migrator to leave my connection open after it finishes, so that I retain ownership of the connection lifecycle.
12. As a developer who did not open the connection, I want the migrator to open and close its own connection, so that no connection leaks.
13. As a developer running migrations against a direct database connection, I want the advisory lock to behave correctly, so that the standard startup deployment is fully protected.
14. As a developer, I want a documented note about the PgBouncer transaction-pooling limitation, so that I know to run migrations against a direct or session-pooled connection.
15. As a maintainer, I want the design and rejected alternatives recorded in an ADR, so that a future reader understands why a session-scoped advisory lock was chosen over a lock table or transaction-scoped lock.
16. As a maintainer, I want an integration test that runs multiple migrators concurrently, so that regressions in the locking behavior are caught automatically.
17. As a developer, I want the single-migration `RunAsync(migration)` primitive to remain lock-free, so that it stays a composable building block and the locking concern lives only in the orchestration methods.

## Implementation Decisions

- **Lock mechanism:** session-scoped `pg_advisory_lock(<key>)` / `pg_advisory_unlock(<key>)`. Chosen over transaction-scoped `pg_advisory_xact_lock` (would release between the per-migration transactions and reopen the race) and over a dedicated lock-table row (bootstrap problem — the migrations schema may not exist yet — and stale-lock recovery on crash). See ADR-0001.
- **Lock scope:** acquired at the very top of both `MigrateDatabaseToLatestAsync` and `MigrateDatabaseToAsync`, before the empty-database check, held across schema application and the entire migration loop, released in a `finally`.
- **Correctness property:** all in-run state reads (`IsDatabaseEmptyAsync`, `RetrieveAlreadyExecutedMigrationsAsync`) already occur after the lock would be held, so a blocked-then-acquired instance re-reads fresh state and correctly finds nothing pending. No code reordering required beyond placing acquisition at the top.
- **Acquisition semantics:** blocking, no timeout, never throws on contention. The acquisition query is issued with an **infinite command timeout** so Npgsql's default 30-second command timeout cannot abort the wait.
- **Lock key:** a single fixed `const long` magic number, defined as a named, commented constant in `DatabaseMigrator`. Not derived/computed at runtime. (A GUID was considered but advisory keys are 64-bit, so a 128-bit GUID does not fit.)
- **Connection lifecycle:** the migrator calls `Open()` at the start and captures the returned boolean per the existing `DatabaseConnection` contract — if `Open()` returns true the migrator opened the connection and must `Close()` it at the end; if false the caller owns it and the migrator must not close it. Mirrors the existing `_transactionOpenedConnection` pattern. The lock is acquired outside any transaction so a transaction rollback cannot release it; an explicit `pg_advisory_unlock` runs in `finally` before the conditional close.
- **Placement:** plain private helper methods on `DatabaseMigrator` (e.g. acquire/release). The advisory lock is **not** extracted into a separate module and **no** public advisory-lock primitive is added to `DatabaseConnection`. Scope stays minimal; honors ADR-0001's "no new public API" decision.
- **Always-on:** no opt-out flag, no constructor parameter, no interface change. `IDatabaseMigrator` is untouched.
- **`RunAsync(migration)`** stays lock-free; locking belongs only to the orchestration methods.
- **Versioning:** MINOR version bump on `mvdmio.Database.PgSQL` (backward-compatible feature). The `mvdmio.Database.PgSQL.Tool` project does not change and is not bumped.
- **Documentation:** README updated with the PgBouncer transaction-pooling caveat (run migrations against a direct or session-pooled connection). ADR-0001 records the decision and rejected alternatives.

## Testing Decisions

- **What makes a good test here:** assert external, observable behavior — that concurrent migration runs leave the database in the correct, exactly-once state — not internal details like whether a specific lock function was called. Timing-based assertions (inspecting `pg_locks`, asserting non-overlap by wall clock) are explicitly avoided as flaky.
- **Module under test:** the `DatabaseMigrator` orchestration behavior under concurrency (Module C). The private lock helpers are not tested in isolation (they were deliberately not extracted).
- **Primary test:** a new sibling integration test class with its **own** Testcontainer (not the rolled-back `TestBase`, which cannot observe cross-session contention). It runs **3 concurrent `DatabaseMigrator` instances** against the same empty database, with a migration set that includes one deliberately **slow migration** (`pg_sleep`) to widen the contention window so a broken implementation cannot pass by luck. Assertions: (a) no instance throws, (b) each migration identifier appears exactly once in `mvdmio.migrations`, (c) the final schema is correct.
- **Prior art:** `SchemaFirstMigrationTests` already follows the "own container, own connections, bypass `TestBase`" pattern and is the template to copy. Tests use xUnit v3 and AwesomeAssertions per repo convention.

## Out of Scope

- Any public advisory-lock API on `DatabaseConnection` (general-purpose advisory locking is a separate, deliberate feature if ever wanted).
- A configurable acquisition timeout or non-blocking try-and-skip behavior (both deliberately rejected — see ADR-0001).
- An opt-out flag for the locking behavior.
- Locking the standalone `RunAsync(migration)` primitive.
- Support for PgBouncer transaction-pooling mode (documented as a deployment constraint, not engineered around).
- Changes to the `mvdmio.Database.PgSQL.Tool` CLI.
- Timing/`pg_locks`-based test assertions.

## Further Notes

- A consumer-configured server-side `statement_timeout` could still cancel the blocking acquisition; this is outside the library's control and noted as a caveat.
- The decision and its rejected alternatives are captured in `docs/adr/0001-advisory-lock-for-migration-runner.md`.
