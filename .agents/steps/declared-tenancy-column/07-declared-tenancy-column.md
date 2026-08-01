# 07 — Document the guarantee, state its limits, and ship it

Status: done

## What to build

The feature works; now a consumer has to be able to find it and form the right expectation before depending on it. This
step is the only one that touches user-facing documentation and the package version, so the docs describe the finished
surface once rather than being rewritten six times.

**The root `README.md`** gains the tenancy claim alongside the nullability and storage claims it already covers: the
attribute as a developer writes it, the table of which generated member gains a parameter for a column inside the primary
key and outside it, and the limits stated plainly — it reaches generated code and stops there, nothing checks the column
against the real table, and nothing checks the value against anything. It makes the tenant impossible to omit; it does not
make it impossible to get wrong. Say so, because the feature is easy to oversell.

**The package `README.md`** (`src/mvdmio.Database.PgSQL/README.md`) is the walkthrough, so it gains more: a tenancy-column
section beside its Column Nullability and Column Storage sections, the `[Column]` row of its attribute table mentioning the
new claim, the three new diagnostics in its build-time diagnostics table, and — under its requirements — the caveat that
generated code lands in the consumer's compilation, so the `required` properties need C# 11 there. Every framework this
package targets defaults above that, but a consumer pinning `LangVersion` lower will not compile.

Both READMEs are user-facing only: no ADR links, no changelog, no roadmap, no test notes.

**`CONTEXT.md`** already carries the **Tenancy column** glossary entry, written during the design session. Read it against
what actually shipped and correct it only if the implementation diverged.

**The analyzer release notes.** `PGSQL0025`, `PGSQL0026` and `PGSQL0027` are sitting in
`AnalyzerReleases.Unshipped.md`. Fold them into `AnalyzerReleases.Shipped.md` under a new release heading matching the
version this ships as, and leave `AnalyzerReleases.Unshipped.md` empty — that is the shape the existing 0.35 entries took.

**The version.** Bump `<PgSqlVersion>` in `Directory.Build.props` by a MINOR step: a Table definition naming no tenancy
column generates what it generates today, so this is additive under the project's pre-1.0 rule.

**The idea file.** `.agents/ideas/declared-tenancy-column.md` was the source of this spec and every open question it
listed is now answered, here or in ADR 0009. Delete it.

## Acceptance criteria

- [ ] The root `README.md` shows `[Column(Tenancy = true)]`, the member table for a tenancy column inside and outside the
      primary key, and the three limits: generated code only, the column never verified, the value never validated.
- [ ] `src/mvdmio.Database.PgSQL/README.md` has a tenancy-column section beside its nullability and storage sections, names
      the claim in its `[Column]` attribute-table row, lists `PGSQL0025`, `PGSQL0026` and `PGSQL0027` with their severities
      in its build-time diagnostics table, and records the C# 11 requirement for the generated `required` properties.
- [ ] Neither README links an ADR, describes tests, or reads as a changelog or roadmap.
- [ ] `CONTEXT.md`'s **Tenancy column** entry matches what shipped.
- [ ] The three new diagnostics appear in `AnalyzerReleases.Shipped.md` under a release heading matching the new version,
      `AnalyzerReleases.Unshipped.md` is empty, and the build reports no `RS2008`.
- [ ] `<PgSqlVersion>` in `Directory.Build.props` is bumped one MINOR step.
- [ ] `.agents/ideas/declared-tenancy-column.md` is gone.
- [ ] `dotnet format --verify-no-changes` exits zero, then `dotnet build` and `dotnet test` are clean, run sequentially and
      never in parallel. Integration tests need Docker running.

## Outcome

Documented and shipped the finished feature; no code changed.

- **Root `README.md`.** Added a tenancy paragraph after the storage-claim paragraph, alongside the existing nullability
  and storage claims: the `[Column(Tenancy = true)]` snippet, a condensed member table (the composite-key/create/update
  rows collapsed to one line each since they don't distinguish inside/outside the key), and the two-sentence limit
  statement lifted from the spec's Solution section.
- **`src/mvdmio.Database.PgSQL/README.md`.** Added a `### Tenancy Column` section after `### Column Storage` and before
  `### Requirements`, covering: the attribute and an example on a composite key, the full member table with the `*`
  footnote for a tenancy column's own unique lookup/delete, the `Query()`/`commandTimeout` note, the `required`-property
  write path and why the data type doesn't get it, the update statement's `SET`→`WHERE` move and the `PGSQL0007`
  consequence, multiple tenancy columns, "everywhere else it's ordinary", the narrow-guarantee paragraph, and the three
  diagnostics with their behaviour (`PGSQL0025`/`PGSQL0026` abandon the table, `PGSQL0027` warns and drops nothing).
  Updated the `[Column]` attribute-table row to mention `Tenancy`, added the three rows to the build-time-diagnostics
  table, and extended the paragraph after that table so the "abandons the table" / "abandons nothing" grouping still
  reads correctly with three more codes in it (`PGSQL0027` called out as a warning that drops nothing, next to
  `PGSQL0011`/`PGSQL0024`; `PGSQL0025`/`PGSQL0026` folded into the "abandons the table" sentence next to `PGSQL0020`).
  Added the C# 11 requirement under `### Requirements`, phrased about the consumer's own compilation rather than this
  package's.
- Neither README links an ADR, mentions a test, or reads as a changelog — checked against the standing "READMEs are
  user-facing only" preference while writing, including catching and removing an ADR 0005 link added in a first draft
  of the diagnostics paragraph.
- **`CONTEXT.md`.** Read the existing **Tenancy column** glossary entry against the shipped implementation (uniform
  rule, `required` property, `PGSQL0025`–`PGSQL0027`, the narrow-guarantee statement) — it already matched exactly what
  steps 02–06 built, so left unchanged.
- **Analyzer release notes.** Added a `## Release 0.36` heading to `AnalyzerReleases.Shipped.md` under the existing
  `0.35` block, carrying `PGSQL0025`–`PGSQL0027` verbatim from `AnalyzerReleases.Unshipped.md`, then emptied
  `AnalyzerReleases.Unshipped.md` back to zero bytes — confirmed against `git show` of the file's state right after the
  0.35 fold-in that "empty" means literally empty, not a header with no rows.
- **Version.** `<PgSqlVersion>` in `Directory.Build.props`: `0.35.0` → `0.36.0`.
- **Idea file.** Deleted `.agents/ideas/declared-tenancy-column.md`.
- `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` (sequential, `DOTNET_ROLL_FORWARD=LatestMajor`
  for the net9.0 test hosts, Docker running) all pass: build 0 warnings/0 errors (no `RS2008`); Unit 197/197,
  Analyzers.Tests 129/129, Integration.OData 134/134, Packaging 13/13, Integration 256/256 — all unchanged from step
  06, as expected since this step touches no code.

No deviations from the step or spec.
