# 06 — A relation that could reach across tenants warns at build time

Status: done

## What to build

The one hole the generated surface cannot close is a **Relation**: a join reaches the target's rows through the
foreign-key columns, and if the tenant is not pinned inside that join, a query over a tenanted table can still pull
another tenant's related rows. `PGSQL0027` makes that visible instead of silent.

It is a **warning**, not an error, and it drops nothing — not the relation, not the table. A relation to a shared,
untenanted table can be perfectly legitimate, and under ADR 0005 a relation-level problem drops the relation rather than
the table, so refusing here would break a legal design.

The rule is the **strict** form, deliberately. A relation always pairs its foreign key positionally against the other
side's primary key:

- **Relation to one row** — the foreign key is on the declaring table. For each tenancy column of the **target**: the
  declaring-side property paired against it must be the declaring table's own tenancy column. If the target's tenancy
  column is not part of the target's primary key, nothing is paired against it at all.
- **Relation to many rows** — the sides swap, because the foreign key lives on the target. For each tenancy column of the
  **declaring** table: the target-side property paired against it must be the target's own tenancy column.

Warn once per tenancy column that comes out unpaired, naming the relation property. A declaring table with no tenancy
column at all falls out of the to-one rule automatically: nothing it holds can be its tenancy column, so a tenanted target
warns.

The loose reading — warn only when *nothing* is paired against the target's tenancy column — is the one to avoid. It would
pass a relation pairing some unrelated `Guid` against the target's tenancy column, which is exactly the reach-through the
check exists to catch. Pin that case with a test.

`RelationResolver` is the only stage that sees more than one table at a time, and it already zips the two sides into the
column pairs the join is made of, so the check belongs there rather than in the per-table parser.

The descriptor needs an entry in `AnalyzerReleases.Unshipped.md` — `RS2008` is enforced for the analyzer project, so the
release notes are part of the change.

Cover it at the generator seam only. `PGSQL0027` is deliberately **not** exercised at the integration seam: a warning in
that project would be a build warning on every run of the suite. Extend `TableRepositoryGeneratorTenancyTests` with the
warning fired and not fired for both cardinalities, the strict form's unrelated-property case, a declaring table with no
tenancy column against a tenanted target, a relation to an untenanted target staying silent, and the relation still being
emitted and registered despite the warning.

## Acceptance criteria

- [ ] `PGSQL0027` is a warning, is reported at the relation property, and drops neither the relation nor the table — the
      relation is still mirrored onto the data type and still registered on the query surface.
- [ ] For a relation to one row it fires unless the declaring-side property paired against each of the target's tenancy
      columns is the declaring table's own tenancy column; for a relation to many the same check runs with the sides
      swapped.
- [ ] A relation pairing an unrelated property against the target's tenancy column warns — the strict form, pinned by its
      own test.
- [ ] A relation whose target declares no tenancy column does not warn, and a relation whose join pins the tenant
      correctly does not warn.
- [ ] A tenancy column that cannot be paired at all — because it is outside the primary key the relation joins on —
      produces exactly one warning naming the relation property.
- [ ] The descriptor is listed in `AnalyzerReleases.Unshipped.md`, and the build reports no `RS2008`.
- [ ] No new build warning appears anywhere in the solution: the existing table definitions in the integration, OData and
      packaging projects declare no tenancy column, and must stay that way.
- [ ] `dotnet format` → `dotnet build` → `dotnet test`, run sequentially and never in parallel, are all clean.
      Integration tests need Docker running.
- [ ] `README.md`, `src/mvdmio.Database.PgSQL/README.md` and `<PgSqlVersion>` in `Directory.Build.props` are untouched —
      the last step of this spec owns all three.

## Outcome

`PGSQL0027` closes the one remaining hole — a relation whose join could reach across tenants — as a warning that drops
neither the relation nor the table, in `src/mvdmio.Database.PgSQL.Analyzers/RelationResolver.cs` and
`TableRepositoryDiagnostics.cs`:

- **The check (`RelationResolver.CheckTenancyPairing`).** Runs at the end of `TryResolve`, after the arity and type
  checks pass and before `ResolvedRelation` is built, using the same `foreignKeyOwner`/`primaryKeyOwner` split the
  arity check already computed (`(target, model)` for a relation to many, `(model, target)` for a relation to one —
  which one holds the primary key the join addresses). For every tenancy column of `primaryKeyOwner`, it finds that
  column's position in the joined primary key by property name; if found, the paired property at that position (on
  the foreign-key side, already zipped positionally into `foreignKeys`) must itself be `IsTenancy`, or it warns. If
  the tenancy column is not part of the joined key at all, it warns unconditionally — the join never touches that
  column, which pins the tenant even less than pairing it against the wrong property would. This is the piece the
  step's "what to build" text states as a fact ("nothing is paired against it at all") but the acceptance criteria
  and ADR 0009 both require to warn, not to skip — a tenancy column outside the joined key produces exactly one
  warning naming the relation property, the same as a wrong pairing.
- **Reported, not returned.** `CheckTenancyPairing` only appends to the diagnostics builder; `TryResolve` still
  returns `true` and the relation is added to `relations` regardless, so nothing about generation changes — the
  relation is still mirrored onto the data type and still registered on the query surface, per ADR 0005.
- **The descriptor (`TableRepositoryDiagnostics.RelationCouldReachAcrossTenants`, PGSQL0027).** `DiagnosticSeverity.Warning`,
  reported at the relation's own location, message names the declaring/target class, the relation property and the
  unpinned tenancy column. New entry in `AnalyzerReleases.Unshipped.md`; the build reports no `RS2008`.

Generator seam: extended `TableRepositoryGeneratorTenancyTests`
(`test/mvdmio.Database.PgSQL.Analyzers.Tests/TableRepositoryGeneratorTenancyTests.cs`, now 39 tests, +9) with:
the warning firing and not firing for a relation to one row (correct pairing silent, an unrelated property paired
warns — the strict form pinned by its own test); the same pair for a relation to many, sides swapped; a declaring
table with no tenancy column at all warning automatically against a tenanted target; a relation to an untenanted
target staying silent; a tenancy column outside the joined key producing exactly one warning; the relation still
being mirrored onto the data type and still registered on the query surface despite the warning; and the warning
shape's generated source compiling. Every new table shape carries at least one ordinary mutable column beyond its key
and tenancy columns, since a table with nothing left to update trips the pre-existing PGSQL0007 and is abandoned
before the relation is ever resolved — confirmed the hard way when two of the first-draft shapes silently abandoned
their table and made their assertions pass vacuously.

No changes at the integration seam, per the step: `PGSQL0027` is deliberately not exercised there, since a warning in
that project would be a build warning on every run of the suite.

`dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` (sequential, `DOTNET_ROLL_FORWARD=LatestMajor`
for the net9.0 test hosts, Docker running) all pass: Unit 197/197, Analyzers.Tests 129/129 (120 prior + 9 new),
Integration.OData 134/134, Packaging 13/13, Integration 256/256 (unchanged). `README.md`,
`src/mvdmio.Database.PgSQL/README.md` and `Directory.Build.props` are untouched, reserved for the spec's last step.

One deviation from the step's literal wording, required by its own acceptance criteria: the "What to build" section
states that when a target's tenancy column sits outside the primary key the relation joins on, "nothing is paired
against it at all" — read in isolation that could mean skip the check. The acceptance criteria and ADR 0009 both
settle it explicitly: that case "produces exactly one warning naming the relation property." Implemented as a warning,
not a skip, and pinned by `RelationTenancyColumn_OutsideTheJoinedKey_ReportsExactlyOnePGSQL0027`.
