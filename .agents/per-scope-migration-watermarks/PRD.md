# PRD: Per-scope migration watermarks

Status: ready-for-agent

## Problem Statement

When two `DatabaseMigrator` instances run against the **same database** for **different assemblies**, not all migrations are applied. The migrator decides what to run by comparing each migration's timestamp **Identifier** against a single **global** `MAX(identifier)` watermark over the whole `mvdmio.migrations` table. If one assembly's migrations carry lower timestamps than another assembly's already-applied migrations, they fall below that global watermark and are **silently skipped** — leaving the database missing schema/data the developer believed had been applied, with no error to signal it.

## Solution

Track the migration watermark **per Scope** instead of globally. Every **Migration** belongs to a **Scope** (by default the simple name of its declaring assembly, overridable on `IDbMigration`). The `mvdmio.migrations` table records the scope of each executed migration, and a migration runs when its identifier is ahead of the **Watermark** — `MAX(identifier)` — *for its own scope*. Scopes advance independently, so one assembly's timeline can never suppress another's, and the silent-skip disappears.

Existing databases upgrade in place: the migrator adds a nullable `scope` column and a temporary, self-healing backfill attributes existing rows to their scope. The change is shipped as a major version because it requires a new constructor dependency and alters the migrations-table shape and several public types. See ADR `docs/adr/0002-per-scope-migration-watermarks.md` and the Scope / Identifier / Watermark glossary in `CONTEXT.md`.

## User Stories

1. As a developer running migrations from two assemblies against one database, I want every assembly's pending migrations to run, so that no migration is silently skipped because another assembly's migrations have higher timestamps.
2. As a developer, I want each migration to belong to a scope that defaults to its declaring assembly's simple name, so that scoping works without me changing any existing migration.
3. As a developer who renamed my migrations assembly, I want to override the scope on `IDbMigration`, so that my migration history stays attached to a stable scope across the rename.
4. As a developer, I want the migrator to determine "already applied" from the highest identifier *within each scope*, so that a migration is run if and only if it is ahead of its own scope's watermark.
5. As a developer with an existing populated database, I want the migrator to add the `scope` column automatically on the next run, so that I do not have to hand-write a schema upgrade.
6. As a developer upgrading a single-assembly database, I want my existing migration rows attributed to the correct scope automatically, so that none of my already-applied migrations re-run.
7. As a developer, I want the backfill to attribute a row only when a discovered migration with the same identifier is present, and to fill only rows whose scope is still empty, so that concurrent runners can each safely fill their own portion.
8. As a developer, I want a clear warning logged when some rows could not be attributed to a scope, so that I know to resolve them manually.
9. As a developer, I want rows that remain unattributed to be excluded from every scope's watermark, so that an un-backfilled row never silently suppresses a migration; if a conflict results, the migration fails loudly.
10. As a developer running integration tests, I want an empty database with multiple schema-first assemblies bootstrapped by a single migrator instance to apply every assembly's embedded schema and record a baseline per scope, so that each scope's post-baseline migrations run correctly.
11. As a developer, I want each applied embedded schema to record a baseline row for its own scope, so that migrations folded into one assembly's schema are not re-run while another assembly still bootstraps.
12. As a developer, I want the generated `schema.sql` header to record one migration-version line per scope, so that a schema-first bootstrap can establish the correct per-scope baselines.
13. As a developer with an older scope-less `schema.sql`, I want the library to still read its header and let the backfill heal the resulting baseline, so that I do not have to regenerate every schema file on upgrade.
14. As a developer using the schema-export/pull tool, I want it to read the highest identifier per scope and emit a per-scope header, so that exported schemas carry complete baseline information.
15. As a developer, I want `MigrateDatabaseToAsync(identifier)` to advance every scope up to the given identifier, so that targeted forward migration has a coherent meaning across scopes.
16. As a developer, I want the migrations table to enforce uniqueness on `(scope, identifier)`, so that the same identifier can legitimately exist in two different scopes.
17. As a developer, I want a freshly created migrations table and an upgraded one to have the same shape, so that the exported schema does not differ depending on how the table came to exist.
18. As a developer, I want the table upgrade to run under the existing migration advisory lock, so that concurrent instances upgrade the table exactly once.
19. As a developer, I want to pass an `ILogger<DatabaseMigrator>` to the migrator, so that migration warnings and diagnostics flow into my application's logging.
20. As a developer, I want `ExecutedMigrationModel` to expose the scope of each executed migration, so that I can inspect per-scope migration state programmatically.
21. As a developer, I want the upgrade documented as a major version with a clear note that the backfill is temporary, so that I know it will be removed and the table tightened in the next major version.
22. As a maintainer, I want the per-scope selection logic isolated as a pure module, so that I can unit-test the watermark behavior without a database.

## Implementation Decisions

**Scope model**
- `IDbMigration` gains a `Scope` property with a default implementation returning the declaring assembly's simple name (`GetType().Assembly.GetName().Name`), overridable — mirroring the existing default-and-override pattern of `Identifier` and `Name`.
- The watermark is **per-scope `MAX(identifier)`**, not exact `(scope, identifier)` membership. Membership was rejected because schema-first bootstrap records a single baseline row per scope to represent all sub-baseline migrations; membership would re-run those against tables the schema already created. (ADR 0002.)
- Renaming an assembly without pinning the scope re-forks history (every migration re-runs); this sharp edge is accepted and documented, with no safeguard.

**Pending-migration selector (new pure module)**
- Input: executed rows (with scope), discovered migrations (with scope), optional global target identifier.
- Output: the ordered set of migrations to run. For each migration, run it when `identifier > MAX(identifier within its scope)` and, when a target is supplied, `identifier <= target` (a global ceiling applied per scope).
- Rows with `NULL` scope are excluded from every concrete scope's watermark.

**Scope backfill matcher (new pure module, temporary, `[Obsolete]`, removed next major)**
- Matches `NULL`-scope executed rows to discovered migrations by identifier; assigns the migration's scope; fills only rows where scope is currently `NULL` (never overwrites). Reports leftover unattributed rows so the orchestrator can emit a single warning.

**Migrations-table manager (new module)**
- `EnsureTable` idempotently creates the table in its new shape and upgrades an existing one: `ADD COLUMN IF NOT EXISTS scope TEXT` (nullable), `DROP CONSTRAINT IF EXISTS migrations_pkey`, add a **named** `UNIQUE (scope, identifier)` index. No primary key this major. Same definition for fresh and upgraded tables.
- Runs under the existing session-scoped advisory lock (ADR 0001).
- Next major (out of scope here): `SET scope NOT NULL` and `ADD PRIMARY KEY USING INDEX` to promote the named unique index to `PRIMARY KEY (scope, identifier)`.

**Schema-first / header**
- Header format: one `-- Migration version: <identifier> (<name>) [<scope>]` line per scope; a single generalized regex reads both the old scope-less single-line form and the new per-scope form.
- `SchemaFileParser.ParseMigrationVersion` returns a collection of `(scope, identifier, name)`; `SchemaFileMigrationInfo` gains a nullable `Scope`.
- `ApplySchemaAsync` records one baseline row per scope (highest identifier per scope across all applied schema headers). Old scope-less headers record a `NULL`-scope baseline that the backfill heals.
- `SchemaExtractor.GetCurrentMigrationVersionAsync` reads the highest identifier per scope (`GROUP BY scope`) and returns a collection feeding the multi-line header.
- The empty-database check (`IsDatabaseEmptyAsync` / `ShouldApplySchemaAsync`) stays **global**. Schema-first bootstrap is supported for the first scope to touch a fresh database, or for multiple assemblies bootstrapped by a single `DatabaseMigrator` instance. A second, separate schema-first migrator instance against an already-populated database is an accepted, documented limitation; a per-scope empty-check is a tracked follow-up.

**Logging**
- New dependency on `Microsoft.Extensions.Logging.Abstractions` (abstractions only, not the full package).
- `DatabaseMigrator` takes a **required** `ILogger<DatabaseMigrator>`, positioned before the `params Assembly[]` argument in all constructors. Source-breaking — contributes to the major version bump.

**Public API / records**
- `ExecutedMigrationModel` gains a nullable `Scope`; `RetrieveAlreadyExecutedMigrationsAsync` selects `scope`.
- `RunAsync` writes `migration.Scope` into the `scope` column on every recorded migration.
- `MigrateDatabaseToAsync(long)` is retained as a global identifier ceiling ("advance every scope up to this identifier"); migrations are up-only.

**Versioning**
- MAJOR bump in `mvdmio.Database.PgSQL` and `mvdmio.Database.PgSQL.Tool` (`.csproj`). README updated. The backfill and global-empty-check limitation are documented.

## Testing Decisions

Good tests assert **external behavior**, not implementation details: given inputs and observable outputs/database state, not internal call sequences. The four extracted modules are designed to be testable in isolation. All four get dedicated tests (confirmed with the developer).

- **Pending-migration selector** — pure unit tests (no database). Cases: two scopes with interleaved identifiers (the core regression — lower-timestamp scope still runs); a scope whose migrations are all below its watermark (none run); global target ceiling applied per scope; `NULL`-scope rows excluded from watermarks; ordering by identifier within the run.
- **Scope backfill matcher** — pure unit tests. Cases: row matched by identifier gets the migration's scope; already-scoped rows untouched; rows with no matching discovered migration left `NULL` and reported; multiple runners (sequential) each fill only their recognized rows.
- **Migrations-table manager** — integration tests (Testcontainers, real PostgreSQL). Cases: fresh create has nullable `scope` + named `UNIQUE (scope, identifier)` + no primary key; upgrade of a legacy `PRIMARY KEY (identifier)` table drops the PK and adds the unique index; idempotent re-run is a no-op; same resulting shape for fresh vs upgraded.
- **Schema-header parser** — pure unit tests. Cases: old single-line scope-less header (scope null); new single per-scope line; multiple per-scope lines; `(none)` header; malformed/absent header.

Prior art in the repo: pure unit tests follow `test/mvdmio.Database.PgSQL.Tests.Unit/Migrations/SchemaFileParserTests.cs` and `MigrationExecutionServiceTests.cs`; database integration tests inherit from `TestBase` and follow `test/mvdmio.Database.PgSQL.Tests.Integration/Migrations/SchemaFirstMigrationTests.cs` (each test runs in a transaction rolled back afterward). Use AwesomeAssertions for assertions. End-to-end schema-first multi-assembly behavior is covered by extending the existing integration suite.

## Out of Scope

- Removing the backfill, making `scope` `NOT NULL`, and promoting the unique index to `PRIMARY KEY (scope, identifier)` — deferred to the next major version (vN+1).
- A per-scope empty-database check to support a second, separate schema-first migrator instance bootstrapping against an already-populated database — tracked follow-up.
- A per-scope target API for `MigrateDatabaseToAsync` (e.g. a per-scope dictionary) — the global identifier ceiling is retained.
- Threading `ILogger` through `DatabaseConnection`, the connectors, and bulk operations — separate work; this change wires logging into `DatabaseMigrator` only.
- Down/rollback migrations — `IDbMigration` remains up-only.
- A safeguard against the assembly-rename-without-pin history re-fork — documented, not guarded.

## Further Notes

- The session-scoped advisory lock from ADR 0001 already serializes migration runs, so the table upgrade and backfill need no additional coordination.
- Cross-scope identifier collisions do not occur in practice (all consumers are controlled, timestamps do not collide); if one ever did, the `UNIQUE (scope, identifier)` index fails the second insert loudly rather than corrupting state.
- Decisions and rejected alternatives are recorded in `docs/adr/0002-per-scope-migration-watermarks.md`; domain vocabulary (Scope, Identifier, Watermark, Migration) is in `CONTEXT.md`.
