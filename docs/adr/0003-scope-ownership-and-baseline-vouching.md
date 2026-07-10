---
status: accepted
---

# Declare scope ownership for schema export and vouch for baselines in schema-first bootstrap

Per-scope watermarks (ADR 0002) made the schema-file header carry one `-- Migration version: <id> (<name>) [<scope>]` line per scope, read by `SchemaExtractor.GetCurrentMigrationVersionAsync` as the highest identifier per scope over the **whole** migrations table. On a database shared by multiple applications, every application's `db pull` therefore exported a header naming **every** application's scope, and `DatabaseMigrator`'s schema-first bootstrap trusted every header line from every applied schema file unconditionally. The combination silently broke a supported scenario (PRD user stories 10/11): a single migrator bootstrapping several assemblies where one assembly has migrations but no schema file of its own — a foreign header line fabricated a baseline for that assembly's scope, and all of its migrations were **silently skipped** (no error, no tables). We decided to fix both sides: the exporter filters the header to **declared owned scopes**, and the bootstrap only records a baseline for a scope the contributing schema file's assembly **vouches for**.

`Scope` is an application-side concept (`IDbMigration.Scope`) that never reaches the live database, so the exporter cannot derive scope↔schema ownership from PostgreSQL catalogs; ownership is declared configuration by design.

## Considered options

- **Producer + consumer fix (chosen).** Producer: an optional `scopes` list in `.mvdmio-migrations.yml` (alongside `schemas`) names the migration scopes the application owns; `db pull` and `db cleanup` thread it to `SchemaExtractor`, which then emits header watermark lines only for owned scopes. Consumer: an applied schema file may only establish baseline rows for scopes its assembly vouches for — the scopes of migrations discovered from that assembly, plus the assembly's simple name (the default scope, which also covers an assembly that folded all of its migrations into its schema). Other scoped header lines are ignored with a logged warning. The producer fix makes the exported file honest; the consumer fix protects against any stale or foreign header regardless of its origin.
- **Producer-side only.** Rejected as sole fix: existing schema files with multi-scope headers stay on disk and in shipped packages; the consumer would keep fabricating baselines from them until every file is regenerated.
- **Consumer-side only.** Rejected as sole fix: the exported files would remain misleading artifacts naming other applications' watermarks.
- **Derive ownership automatically from the database.** Rejected: impossible from system catalogs — the migrations table records scope names but nothing links a scope to the physical schemas a pull exports.

## Consequences

- **Opt-in, backward compatible.** With no `scopes` declared, the exported header is unchanged (all scopes). Declaring ownership only removes foreign lines; an owned scope with no executed migrations degrades to the `(none)` header form, exactly like an empty database, so a schema-first bootstrap of that application alone stays self-consistent.
- **Legacy scope-less rows and header lines are untouched.** Scope-less migration rows (not yet backfilled) keep their header representation regardless of the ownership filter — they cannot be attributed to a scope at export time — and legacy scope-less header lines still record a scope-less baseline healed by the backfill (ADR 0002).
- **Vouching is per file.** A baseline for a scope is the highest identifier for that scope across the header lines of *vouching* files only; a non-vouching file's line for the same scope is ignored (and warned about) even when another file legitimately establishes that scope's baseline.
- **Accepted limitation.** An assembly that folded *all* of its migrations into its schema *and* overrides `IDbMigration.Scope` to something other than its assembly simple name has no discovered migration carrying that scope, so its own header line cannot be vouched for: the baseline is rejected with the warning, and later migrations in that scope fail loudly (objects already exist) instead of being silently skipped. Loud failure is the intended trade-off; declaring the scope on a discovered migration or keeping the default scope avoids it.
- **API surface.** Additive only: `SchemaExtractor` gains a constructor overload and an `OwnedScopes` property; `ToolConfiguration` gains `Scopes`. No breaking change (MINOR bump).
- The per-scope empty-database check (a second, separate schema-first migrator against an already-populated database) remains the tracked follow-up from ADR 0002 and is unaffected.
