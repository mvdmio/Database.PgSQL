# A migration can run outside a transaction, on the migrator's own connection

The migrator runs each **Migration** and its history row in one transaction. Every lock a statement takes is held until that transaction commits. On a large table that receives writes all the time, an `ALTER TABLE` followed by a back-fill and an index build keeps an `ACCESS EXCLUSIVE` lock for the whole run. The application's old version is still serving during a deploy, and its writes wait for that lock. mvdmio-suite #1339 blocked Request ingest this way, on a table of about 8.9M rows. PostgreSQL also refuses `CREATE INDEX CONCURRENTLY` inside a transaction block, so a migration could not build an index without blocking writes.

We decided that a migration can opt out with `IDbMigration.RunsInTransaction`, which defaults to `true`. When it is `false`, `UpAsync` gets the migrator's own connection with no transaction open, so each statement commits on its own and releases its locks when it finishes. The migrator writes the history row only after `UpAsync` returns. A migration that fails partway has no row, so it runs again in full at the next migrate. Such a migration must therefore be safe to re-run from any point it can stop at, which goes further than the replay-safety every migration already needs.

## Considered options

- **Opt out on the migration, same connection (chosen).** The connection already holds the session-scoped advisory lock from [ADR 0001](0001-advisory-lock-for-migration-runner.md), so runs stay serialized with nothing new to coordinate. Existing migrations are unchanged, because the default keeps today's behaviour.
- **Move every slow step into an application job after boot.** mvdmio-suite #1415 did this for #1339's `requests` table. It works, but each such change needs application code that copes with a half-finished schema until the job is done. It stays the answer only for work longer than the deploy waits for the application to start, because the migrator still runs before the application serves.
- **A second connection for the non-transactional work.** Rejected. The migrator holds a `DatabaseConnection` and no connection string. A second connection would also sit outside the advisory lock, so two instances could run the same work at once.
- **Detect `CONCURRENTLY` and skip the transaction automatically.** Rejected. `UpAsync` is code, not SQL the migrator can read in advance, and a long back-fill needs the opt-out just as much as an index build does.

## Consequences

- A caller that runs the migrator inside a transaction of its own still runs a non-transactional migration inside that transaction. `CONCURRENTLY` then fails with PostgreSQL's own "cannot run inside a transaction block" error. The library adds no check for this.
- `CREATE INDEX CONCURRENTLY IF NOT EXISTS` is not safe to re-run. A failed build leaves an invalid index, and `IF NOT EXISTS` then skips it. A re-runnable migration drops the index with `DROP INDEX CONCURRENTLY IF EXISTS` before it builds it.
