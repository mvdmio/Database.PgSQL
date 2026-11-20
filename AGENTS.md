# mvdmio.Database.PgSQL

C# NuGet package wrapping Dapper for PostgreSQL — connections & query execution, transactions, migrations, bulk ops (copy/upsert), and management tasks (schema/table existence). Ships as the `mvdmio.Database.PgSQL` library plus the `mvdmio.Database.PgSQL.Tool` (`db`) CLI. Multi-targets `net10.0;net11.0` with `LangVersion=latest`.

## Shared rules

- Work on the repo's default branch unless a skill or the user names a branch. This overrides any default that creates branches.
- Preserve the worktree: parallel sessions share each checkout. Inspect existing changes before editing and leave them intact. When the index already holds another session's staged changes, commit with `git commit -- <your paths>`; for a file another session also edited, stage only your hunks (`git apply --cached` of your filtered diff) and run plain `git commit`.
- Search early. Quote exact errors.
- Ask when blocked, or when the design has more than one reasonable path.
- Telegraph style in replies and agent docs: drop filler and grammar.
- The main session orchestrates: unless the task is trivial, delegate exploring, implementing, testing and reviewing to subagents, at a model and effort that fit the task.
- [judgement] Prefer small diffs over broad refactors. Keep files under ~500 lines; tests may exceed it. Add an abstraction only once a second use exists. Ship no backward-compat shim unless real consumers or persisted data need one.
- Every feature and every bug fix comes with a test; a bug fix's test fails without the fix.
- Build, then test, one at a time: parallel builds deadlock on file locks. A build that fails on a file another process holds: stop that process, retry.
- Writing or reviewing C# → [`.agents/refs/csharp-style.md`](.agents/refs/csharp-style.md).

## Essentials

- **Build tool:** .NET SDK (`dotnet`). Package manager: NuGet.
- **Build:** `dotnet build` • **Format:** `dotnet format` • **Test:** `dotnet test` — integration tests need **Docker running** (Testcontainers).
- Before finishing any change, run in sequence: `dotnet format` → `dotnet build` → `dotnet test`. Fix all build errors and test failures before finishing.
- **`dotnet format --verify-no-changes` must exit zero before a change is finished.** CI runs the same command as the first gate on publishing, and a red format check blocks the package from reaching NuGet.org — treat a format failure as a release blocker, not a style nit.
- This is a published NuGet library: treat the public API as a contract. No API changes unless intentional and called out (a breaking version bump).
- **Always update `README.md`** to reflect changes, and **bump `<PgSqlVersion>` in `Directory.Build.props`** (semver, except that a breaking change bumps MINOR while on 0.x — see Common tasks) when the library or tool changes.
- Write tests before or alongside the change (TDD).

## Reference docs

Read the relevant file before working in that area:

- [Architecture & layout](.agents/refs/architecture.md) — project structure, the two packages, entry point, migration framework, key files
- [Coding conventions](.agents/refs/conventions.md) — formatting (3-space, LF), naming, visibility, docs, NuGet-contract rule
- [Testing](.agents/refs/testing.md) — unit & integration patterns, `TestBase`, the `SecondarySchema` project, run commands
- [Common tasks](.agents/refs/common-tasks.md) — adding a migration, using the `db` tool, changing the migration framework, bumping the version
- [Dependencies & CI/CD](.agents/refs/dependencies.md) — package list, centralized version, publish pipeline

## Agent skills

### Tracker

Work is tracked as GitHub Issues in `mvdmio/Database.PgSQL`, with statuses as labels. See `.agents/refs/tracker.md`.

### Domain docs

Single-context (`CONTEXT.md` + `docs/adr/` at the repo root). See `.agents/refs/domain.md`.
