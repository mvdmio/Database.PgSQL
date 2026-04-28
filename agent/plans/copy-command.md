# Plan: `db copy` Command

Add a `db copy` command to `mvdmio.Database.PgSQL.Tool` that copies data from a source PostgreSQL database to a destination PostgreSQL database. Primary use case: refreshing test/staging databases from production snapshots.

## Goals (v1)

- Copy table data from source → destination across all tables in the configured `schemas` (or all user schemas when `schemas` is empty/omitted).
- Truncate destination tables before copy.
- Reuse existing `connectionStrings` env names from `.mvdmio-migrations.yml` for both source and destination.
- Stream rows via existing `BulkConnector` infrastructure (binary `COPY`).
- No safety guard, no data transformation in v1.

## Non-Goals (v1)

- Schema migration / DDL transfer. Destination schema must already exist (typically created via `db migrate latest`).
- Anonymization / column-level transformations.
- Production-write protection / interactive confirmation.
- Resumable / incremental copy.
- Cross-version PostgreSQL compatibility beyond what Npgsql + COPY already supports.

## CLI Surface

```
db copy --from <env> --to <env> [--schemas <s1,s2>] [--exclude-tables <t1,t2>] [--batch-size <n>]
```

| Option / Argument            | Type        | Required | Description                                                                                                          |
| ---------------------------- | ----------- | -------- | -------------------------------------------------------------------------------------------------------------------- |
| `--from`, `-f`               | string      | yes      | Source environment name (key in `connectionStrings`).                                                                 |
| `--to`, `-t`                 | string      | yes      | Destination environment name (key in `connectionStrings`).                                                            |
| `--schemas`                  | string list | no       | Override config `schemas`. Comma-separated. Empty/omitted → fall back to config; if config also empty → all user schemas. |
| `--exclude-tables`           | string list | no       | Fully-qualified `schema.table` entries to skip (e.g. the migration history table).                                    |
| `--batch-size`               | int         | no       | Reporting / progress batch size for log output. Default 10000. Does not change COPY semantics.                        |

Source and destination must be different connection strings. The command will refuse to run if `--from` and `--to` resolve to identical connection strings (string-equality check; sufficient for v1).

The migration history table (`__migrations` or whatever `DatabaseMigrator` uses) is implicitly excluded so the destination's migration state is preserved.

## Behavior

1. Load `ToolConfiguration`. Resolve both connection strings via `ConnectionStringResolver`.
2. Open both `DatabaseConnection`s (`await using`).
3. Determine schemas to operate on:
   - CLI `--schemas` wins, then config `schemas`, else all user schemas (excluding `pg_*`, `information_schema`).
4. Inspect source via `SchemaExtractor` (already used by `SchemaExportService`) to enumerate tables in those schemas. Topologically sort by FK dependency (or rely on `session_replication_role = replica` to disable triggers/FKs during the copy — see "FK strategy" below).
5. Validate destination has the same set of tables and columns. On mismatch, print which tables/columns are missing and abort. (User is expected to run `db migrate latest` against destination first.)
6. For each table:
   - `TRUNCATE <schema>.<table> CASCADE` on destination.
   - Stream rows source → destination using `COPY ... TO STDOUT (FORMAT BINARY)` and `COPY ... FROM STDIN (FORMAT BINARY)`. See "COPY pipe strategy" below.
   - Report rows copied.
7. On any error: abort, propagate exception. Each table is its own transaction on the destination so partial progress can be diagnosed.
8. Print a summary: tables copied, total rows, elapsed time per table and overall.

### FK strategy

Two viable options. Plan recommends **option A** for v1 simplicity.

- **A. Session replication role.** On the destination connection, run `SET session_replication_role = replica;` for the duration of the session. This disables triggers and FK checks. Requires superuser or `pg_write_server_files` / equivalent — same privilege normally required to truncate user tables in test envs. Document this requirement in README.
- **B. Topological sort.** Use FK metadata to order tables and copy in dependency order. Requires no special privileges but breaks on cycles (which exist in real schemas via deferrable FKs). Postpone to v2.

### COPY pipe strategy

Use Npgsql's binary COPY APIs directly through the existing `DatabaseConnection.Connection` accessor (already exposed `internal`).

- Source: `await using var reader = await sourceConn.BeginBinaryExportAsync($"COPY {qualifiedTable} TO STDOUT (FORMAT BINARY)", ct);`
- Destination: `await using var writer = await destConn.BeginBinaryImportAsync($"COPY {qualifiedTable} FROM STDIN (FORMAT BINARY)", ct);`
- Loop: `while (await reader.StartRowAsync(ct) != -1) { await writer.StartRowAsync(ct); for each column: read raw, write raw via NpgsqlDbType + value }`

Because column-by-column raw transfer requires knowing types, fetch column metadata once per table from `information_schema.columns` (or reuse `SchemaExtractor`) and use `reader.Read<object>(NpgsqlDbType)` paired with `writer.Write(value, NpgsqlDbType)`. Nulls handled via `reader.IsNull` and `writer.WriteNull()`.

Add a new helper to the main library: `BulkConnector.CopyFromAsync(...)` that takes a peer `DatabaseConnection` and a table name. This keeps the streaming logic reusable and unit-testable, and aligns with the `BulkConnector` pattern of high-level COPY operations. The CLI handler then becomes a thin orchestrator over this helper.

## Configuration Changes

No required changes to `.mvdmio-migrations.yml`. The existing `schemas` and `connectionStrings` fields are sufficient.

Optional v1 addition (to keep scope tight, defer if desired):

```yaml
copy:
  excludeTables:
    - public.__migrations
    - public.audit_log
```

If added, the CLI `--exclude-tables` is unioned with `copy.excludeTables`. Defer to v1.1 if it adds noise to the implementation.

## Code Changes

### Main library (`src/mvdmio.Database.PgSQL/`)

1. **`Connectors/Bulk/BulkConnector.cs`** — add `CopyFromAsync(DatabaseConnection source, string schema, string table, IReadOnlyList<string> columns, CancellationToken ct)` that streams binary COPY between connections. Returns `long` row count. Lives in `BulkConnector` because it owns the destination connection.
2. **`Connectors/Bulk/`** — add new internal helper `CrossConnectionCopySession` if the streaming logic exceeds ~50 LOC, to keep `BulkConnector.cs` under 500 LOC (per AGENTS rules).
3. **`Connectors/Schema/SchemaExtractor`** (already exists) — verify it exposes per-table column metadata in declared order. Add helper if not.

### Tool project (`src/mvdmio.Database.PgSQL.Tool/`)

1. **`Commands/CopyCommand.cs`** — new file. Defines options, wires to `CopyHandler`. Follow `PullCommand.cs` pattern.
2. **`Program.cs`** — register `rootCommand.Subcommands.Add(CopyCommand.Create());`.
3. **`Copy/CopyHandler.cs`** — new feature folder + handler. Two ctors (default + DI). Internal interfaces in same file:
   - `ICopyReporter` (progress/start/finish per table, summary).
   - `ICopyDatabaseFactory` (creates source + destination `DatabaseConnection` — testable seam).
   - `ConsoleCopyReporter` default impl.
4. **`Copy/CopyService.cs`** — orchestrator that:
   - Resolves table list via `SchemaExtractor`.
   - Validates destination schema parity.
   - Iterates tables, calls `BulkConnector.CopyFromAsync`.
   - Manages `session_replication_role` on destination.
5. **Reuse** `ToolConfigurationLoader`, `ConnectionStringResolver` as-is.

### Tests

Unit tests (`test/mvdmio.Database.PgSQL.Tests.Unit/`):

- `CopyCommandTests` — option parsing, missing `--from`/`--to`, identical-connection rejection, env-not-found error message lists available envs.
- `CopyHandlerTests` — orchestrator with mocked `ICopyDatabaseFactory` and `ICopyReporter`; verifies table iteration order, truncate-then-copy sequence, exclude-table filtering, summary computation.

Integration tests (`test/mvdmio.Database.PgSQL.Tests.Integration/`):

- Spin up two PostgreSQL containers (or two databases on one container).
- Apply same schema to both.
- Seed source with rows of varied types (int, text, jsonb, uuid, arrays, timestamps, nullable).
- Run copy.
- Assert destination row counts and contents match source.
- Verify FKs work after copy (insert dependent row succeeds).
- Verify `__migrations` table is preserved (i.e. excluded by default).
- Verify schema mismatch (extra column in source) produces a clear error before any truncate.

## Documentation

1. **`src/mvdmio.Database.PgSQL.Tool/README.md`** — add `db copy` section with examples, prerequisites (destination schema must exist; user must have privileges to truncate and to set `session_replication_role`).
2. **Root `readme.md`** — mention the new command in the typical workflow section.
3. **AGENTS.md** — no change required.

## Versioning

Per `AGENTS.md`, bump MINOR version (new backward-compatible functionality):

- `mvdmio.Database.PgSQL.csproj` — bump MINOR (new `BulkConnector.CopyFromAsync` API).
- `mvdmio.Database.PgSQL.Tool.csproj` — bump MINOR (new command).

## Implementation Order

1. Add `BulkConnector.CopyFromAsync` + unit/integration tests in main library.
2. Add `CopyCommand` skeleton + register in `Program.cs`.
3. Implement `CopyHandler` + `CopyService` against single-DB integration test (source and dest as two databases on one container).
4. Wire `session_replication_role` and excluded-tables logic.
5. Schema parity validation.
6. Polish: progress reporting, summary output.
7. Documentation + version bumps.
8. `dotnet format && dotnet build && dotnet test` until green.

## Open Questions / Risks

- **Privileges.** `session_replication_role = replica` typically needs superuser. If destination is a non-superuser test DB, copy will still work but FK violations will surface during cross-table copy. Mitigation: document; future v2 could add topological sort fallback.
- **Large tables.** Binary COPY between connections is fast but holds two connections open per table. Acceptable for v1; could parallelize tables in v2.
- **Sequences.** After copy, sequences on destination still hold old values. Add a post-copy step that resets each sequence to `MAX(id) + 1` for serial/identity columns. Should be in v1 — mark as a sub-task in implementation step 4.
- **Generated columns.** `GENERATED ALWAYS AS IDENTITY` and `GENERATED ALWAYS AS (...) STORED` cannot be inserted into directly. Detect via `information_schema.columns.is_generated` and exclude from the COPY column list (Postgres COPY rejects writes to generated columns). Add to step 1.
