# Changelog

## 2026-10-07: Late-merged migrations now run
A migration merged from another branch now runs at the next migrate, even when a higher-numbered migration in the same scope already ran. Each one runs with a warning in the log and in `db migrate` output, and the first migrate after upgrading also runs any that were skipped before. `db cleanup` no longer deletes a migration file that any configured environment still has to run.
