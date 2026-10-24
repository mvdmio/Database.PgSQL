# Changelog

## 2026-10-24: A cancelled call throws OperationCanceledException
A cancelled async call on `DatabaseConnection` (`Dapper.ExecuteAsync`, `QueryAsync`, and the other Dapper calls) now throws `OperationCanceledException` instead of a `QueryException` that wrapped it. `catch (OperationCanceledException) when (ct.IsCancellationRequested)` now works for Dapper calls as it already did for LINQ calls. A SQL error, a timeout, or a lost connection still throws `QueryException` with the SQL attached.

## 2026-10-17: InTransaction now reports the original failure
When the database connection breaks inside `InTransaction`, `InTransactionAsync`, or `InTransactionAsync<T>`, the call now throws the exception your action saw. Before, the failed rollback hid it behind an `ObjectDisposedException`. The `DatabaseConnection` also stays usable afterwards: `RollbackTransaction` and `RollbackTransactionAsync` now always clear the transaction, so the next query or transaction starts on a fresh connection.

## 2026-10-09: Connection pools now capped and named
Every connection pool the library builds now holds at most 10 connections, down from 100, so that several programs can share one Postgres server without using up its connection limit. Each connection also shows the name of the program's entry assembly in `pg_stat_activity.application_name`. A program that needs more connections can add `Maximum Pool Size` to its connection string, or set the cap and the name once with `DatabaseConnectionFactorySettings` on the connection factory or in `AddDatabase`.

## 2026-10-07: Late-merged migrations now run
A migration merged from another branch now runs at the next migrate, even when a higher-numbered migration in the same scope already ran. Each one runs with a warning in the log and in `db migrate` output, and the first migrate after upgrading also runs any that were skipped before. `db cleanup` no longer deletes a migration file that any configured environment still has to run.
