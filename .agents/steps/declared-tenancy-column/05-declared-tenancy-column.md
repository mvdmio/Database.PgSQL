# 05 — A tenancy declaration the generator cannot honour abandons the table

Status: done

## What to build

Two declarations are refused at build time, and each abandons the whole table rather than generating it unguarded.
Generating it anyway would hand the developer back precisely the surface this feature removes, and would do it quietly —
so these follow the malformed-key rule, not the relation rule.

- **`PGSQL0025` — a nullable tenancy column.** A null tenant matches no row, so every generated member over the table
  would return nothing. This is the same reasoning that already refuses a nullable primary-key member. Refuse it whether
  the nullability comes from the property's type or from a `Null = true` claim on the same `[Column]`: both mean the column
  can hold null, and the consequence is identical.
- **`PGSQL0026` — `Tenancy = true` on a `[Generated]` column.** A generated column is on no command type, so there is no
  property to make `required` — and the developer would learn that at run time instead of build time.

Both are errors, both point at the property rather than the class, and both stop the table generating anything. Where a
property is malformed in more than one way the existing diagnostics still come out right: a nullable property that is both
a key member and a tenancy column should report one clear reason, not two competing ones.

Both descriptors need an entry in `AnalyzerReleases.Unshipped.md`. That file is an `AdditionalFiles` item and
`EnforceExtendedAnalyzerRules` is on for the analyzer project, so a new descriptor without a release entry warns at build
(`RS2008`) — the release notes are part of the change, not an afterthought.

Cover it at the generator seam only. Extend `TableRepositoryGeneratorTenancyTests` with each diagnostic fired and each
not fired: the nullable case from a nullable type and from a `Null = true` claim, the generated-column case, and the
well-formed counterparts that must stay silent. Assert that a refused table emits nothing at all, the way the
composite-key tests assert it for a malformed key.

There is nothing to add at the integration seam — a refused declaration is a build error, and the integration project must
keep building.

## Acceptance criteria

- [ ] `PGSQL0025` is an error reported on the property, fires for a tenancy column whose type can hold null and for one
      carrying a `Null = true` claim, and abandons the table so nothing is emitted for it.
- [ ] `PGSQL0026` is an error reported on the property, fires for `Tenancy = true` on a `[Generated]` column, and abandons
      the table so nothing is emitted for it.
- [ ] Neither fires on a well-formed tenancy declaration, and a nullable key member that is also a tenancy column still
      reports one clear reason rather than two.
- [ ] Both descriptors are listed in `AnalyzerReleases.Unshipped.md`, and the build reports no `RS2008`.
- [ ] Generator tests cover each diagnostic fired and each not fired.
- [ ] Every existing analyzer and integration test still passes, and a table declaring no tenancy column still emits
      exactly what it emits today.
- [ ] `dotnet format` → `dotnet build` → `dotnet test`, run sequentially and never in parallel, are all clean.
      Integration tests need Docker running.
- [ ] `README.md`, `src/mvdmio.Database.PgSQL/README.md` and `<PgSqlVersion>` in `Directory.Build.props` are untouched —
      the last step of this spec owns all three.

## Outcome

Two diagnostics abandon a table whose tenancy declaration cannot be honoured, in
`src/mvdmio.Database.PgSQL.Analyzers/TableRepositoryDiagnostics.cs` and `TableDefinitionParser.cs`:

- **`PGSQL0025` — nullable tenancy column.** Checked as `properties.Where(x => x.IsTenancy && !x.IsDeclaredNotNull)`.
  `IsDeclaredNotNull` (via `NullabilityClaim.Read`) already folds a property's type and a `Null = true` claim into one
  resolved answer — a dropped contradiction falls back to the type — so this one condition catches both sources the
  step calls out (a nullable type, and a `Null = true` claim on an otherwise non-nullable reference type) without
  checking them separately.
- **`PGSQL0026` — `Tenancy = true` on a `[Generated]` column.** Checked as `properties.Where(x => x.IsTenancy &&
  x.IsGenerated)`.
- **Placement and precedence.** Both checks sit right after the existing nullable-primary-key check
  (`NullablePrimaryKeyProperty`/`PGSQL0020`) and before `duplicateColumn`, each returning early (abandoning the table)
  the same way a malformed key does. Because the primary-key check runs first and already returns on a nullable key
  member, a property that is both a nullable key member and a tenancy column reports only `PGSQL0020` — `PGSQL0025`
  never runs for it, satisfying the step's "one clear reason, not two" requirement without any extra logic to
  suppress a second diagnostic.
- Both descriptors are new entries in `AnalyzerReleases.Unshipped.md` (previously empty); the build reports no
  `RS2008`.

Generator seam: extended `TableRepositoryGeneratorTenancyTests`
(`test/mvdmio.Database.PgSQL.Analyzers.Tests/TableRepositoryGeneratorTenancyTests.cs`, now 30 tests, +5) with: a
nullable-type tenancy column reporting `PGSQL0025` and generating nothing; a non-nullable-typed tenancy column
carrying `Null = true` reporting the same and generating nothing; a `[Generated]` tenancy column reporting `PGSQL0026`
and generating nothing; a nullable primary key that is also a tenancy column reporting only `PGSQL0020` (asserting
`PGSQL0025` is absent); and the existing well-formed `TENANCY_INSIDE_KEY`/`TENANCY_OUTSIDE_KEY` shapes asserted to
report neither new diagnostic.

No changes at the integration seam, per the step: a refused declaration is a build error, so there is nothing for the
integration project to exercise, and it must keep building — confirmed since `Integration` stayed at 256/256, the
same count step 04 left it at.

`dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` (sequential, `DOTNET_ROLL_FORWARD=LatestMajor`
for the net9.0 test hosts, Docker running) all pass: Unit 197/197, Analyzers.Tests 120/120 (115 prior + 5 new),
Integration.OData 134/134, Packaging 13/13, Integration 256/256 (unchanged). `README.md`,
`src/mvdmio.Database.PgSQL/README.md` and `Directory.Build.props` are untouched, reserved for the spec's last step.

No deviations from the step or spec.
