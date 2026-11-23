# The history table's unique index keeps its name when it gains the phase column

Each part of a **Migration** writes its own history row ([ADR 0014](0014-a-migration-holds-both-deploy-phases.md)), so the unique index on `(scope, identifier)` has to widen to `(scope, identifier, phase)`. Several applications can share one database ([ADR 0003](0003-scope-ownership-and-baseline-vouching.md)), and they upgrade the library at different times.

We decided that the table upgrade drops `migrations_scope_identifier_key` and creates it again on `(scope, identifier, phase)` under the same name. An older version checks the table's shape by the index's name only. It finds the name, accepts the table, and issues no DDL. Its own pending logic counts a migration as run once any row for the identifier exists, so two rows for one migration do not confuse it. The new version's shape check reads the index's columns as well as its name.

## Considered options

- **Same name, new columns (chosen).** Older and newer versions can migrate the same database in any order.
- **A new index name, dropping the old one.** Rejected. An older version would not find its index and would run `CREATE UNIQUE INDEX IF NOT EXISTS migrations_scope_identifier_key ON (scope, identifier)`. On a table holding both rows of one migration that build fails, and the older version could no longer migrate at all.

## Consequences

- The index name no longer describes its columns. Nothing reads the name except the shape checks.
- `phase` stays nullable, and null means pre-deploy, because an older version's `INSERT` names no `phase` column.
