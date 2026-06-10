# mvdmio.Database.PgSQL

C# NuGet package wrapping Dapper for PostgreSQL — connections & query execution, transactions, migrations, bulk ops (copy/upsert), and management tasks (schema/table existence). Ships as the `mvdmio.Database.PgSQL` library plus the `mvdmio.Database.PgSQL.Tool` (`db`) CLI. Multi-targets `net8.0;net9.0;net10.0` with `LangVersion=latest`. Work style: telegraph, low-filler, direct.

## Essentials

- **Build tool:** .NET SDK (`dotnet`). Package manager: NuGet.
- **Build:** `dotnet build` • **Format:** `dotnet format` • **Test:** `dotnet test` — integration tests need **Docker running** (Testcontainers).
- Before finishing any change, run in sequence: `dotnet format` → `dotnet build` → `dotnet test`. Run `dotnet` steps **sequentially, never in parallel** — overlapping runs cause file locks/deadlocks. If a build fails on a file lock, stop the locking process and retry. Fix all build errors and test failures before finishing.
- This is a published NuGet library: treat the public API as a contract. No backward-compat shims and no API changes unless intentional and called out (a major version bump).
- **Always update `README.md`** to reflect changes, and **bump `<PgSqlVersion>` in `Directory.Build.props`** (semver: MAJOR = incompatible API; MINOR = backward-compatible feature; PATCH = fix) when the library or tool changes.
- Tests cover all new/changed code — write them before or alongside the change (TDD).

## Universal rules

- Ask if you need clarification or the design is unclear.
- Search early. Quote exact errors. Prefer newer sources.
- Check the worktree first. Do not revert user changes.

## Reference docs

Read the relevant file before working in that area:

- [Architecture & layout](.agents/ref/architecture.md) — project structure, the two packages, entry point, migration framework, key files
- [Coding conventions](.agents/ref/conventions.md) — formatting (3-space, CRLF), naming, visibility, docs, NuGet-contract rule
- [Testing](.agents/ref/testing.md) — unit & integration patterns, `TestBase`, the `SecondarySchema` project, run commands
- [Common tasks](.agents/ref/common-tasks.md) — adding a migration, using the `db` tool, changing the migration framework, bumping the version
- [Dependencies & CI/CD](.agents/ref/dependencies.md) — package list, centralized version, publish pipeline

## Agent skills

### Issue tracker

Issues and PRDs live as markdown files under `.agents/<feature>/`. See `.agents/ref/issue-tracker.md`.

### Triage labels

Default vocabulary (needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix). See `.agents/ref/triage-labels.md`.

### Domain docs

Single-context (`CONTEXT.md` + `docs/adr/` at the repo root). See `.agents/ref/domain.md`.
