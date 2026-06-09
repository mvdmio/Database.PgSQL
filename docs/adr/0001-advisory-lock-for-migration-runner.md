---
status: accepted
---

# Serialize migration runs with a session-scoped PostgreSQL advisory lock

When multiple instances of an application start at once, each constructs a `DatabaseMigrator` and calls `MigrateDatabaseToLatestAsync`/`MigrateDatabaseToAsync` near-simultaneously. Without coordination they race: several pass the empty-database check and all apply the embedded schema, or several try to run and record the same migration row, causing duplicate-key failures and partially-applied state. We decided to guard the entire migration orchestration with a single **session-scoped** PostgreSQL advisory lock (`pg_advisory_lock`) on a fixed `const long` key, acquired before the empty-database check and held across schema application and the full migration loop, released in a `finally`. Acquisition **blocks indefinitely** (issued with an infinite command timeout so Npgsql's default 30s timeout cannot abort the wait) and never throws on contention. Because every in-run state read (`IsDatabaseEmptyAsync`, `RetrieveAlreadyExecutedMigrationsAsync`) happens *after* the lock is held, an instance that waits and then acquires the lock re-reads fresh state and correctly finds nothing left to do. The feature is always-on with no configuration and no public API change; the lock is acquired on the migrator's own connection (opened at the start, closed only if the migrator opened it).

## Considered options

- **Session-scoped `pg_advisory_lock` (chosen).** Auto-released if the holding backend/connection dies, so a crashed instance cannot wedge the lock — no manual cleanup. Held across the whole run regardless of the per-migration transaction boundaries.
- **`pg_advisory_xact_lock` (transaction-scoped).** Rejected: each migration commits in its own transaction, and the empty-check/schema-apply happens before any migration transaction, so a transaction-scoped lock would release between steps and reopen the race.
- **A row in a dedicated lock table (`SELECT … FOR UPDATE` / lock row).** Rejected: requires the table to exist before locking (bootstrap problem — the migrations schema may not exist yet), and a crashed holder can leave a stale lock that needs manual recovery. Advisory locks have neither problem.
- **Non-blocking `pg_try_advisory_lock` + skip.** Rejected: a skipping instance would proceed to boot its application against a database another instance is still mid-migrating — a half-applied schema. Blocking guarantees no instance proceeds until migrations are settled.
- **Blocking with an acquisition timeout that throws.** Rejected: migration duration is unbounded and legitimately long; a timeout would throw on a healthy, slow migration held by a peer. Blocking without a timeout is the correct semantic.

## Consequences

- The migrator holds one connection open for the duration of a run. Behind **PgBouncer in transaction-pooling mode** session-level advisory locks do not work (statements may land on different backends); migrations must be run against a direct connection or session-pooling mode. Documented in the README.
- A consumer-configured server-side `statement_timeout` could still cancel the blocking acquisition; this is outside the library's control.
