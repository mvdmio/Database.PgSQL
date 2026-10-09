# Changelog

## 2026-10-09: Connection pools now capped and named
Every connection pool the library builds now holds at most 10 connections, down from 100, so that several programs can share one Postgres server without using up its connection limit. Each connection also shows the name of the program's entry assembly in `pg_stat_activity.application_name`. A program that needs more connections can add `Maximum Pool Size` to its connection string, or set the cap and the name once with `DatabaseConnectionFactorySettings` on the connection factory or in `AddDatabase`.

## 2026-10-07: Late-merged migrations now run
A migration merged from another branch now runs at the next migrate, even when a higher-numbered migration in the same scope already ran. Each one runs with a warning in the log and in `db migrate` output, and the first migrate after upgrading also runs any that were skipped before. `db cleanup` no longer deletes a migration file that any configured environment still has to run.
