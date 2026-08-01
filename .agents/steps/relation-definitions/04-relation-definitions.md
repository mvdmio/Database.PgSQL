# 04 — What the pairs have to claim: uniqueness and tenancy

Status: done

## What to build

The old mechanism checked a relation by counting foreign-key properties against the target's primary key. Pairs make
that count meaningless, and the two things it was really protecting have to be stated directly against the pairs
instead: that a relation to one row reaches one row, and that a relation on a multi-tenant schema cannot reach another
tenant's rows.

**Reaching one row.** A relation to one row must pair against a set of target columns containing something the target
claims unique — its primary key, or a column marked `[Unique]`. A superset of a unique set is still unique and passes.
This is a claim, not a check, exactly like every other claim in a Table definition, so pairing against nothing unique
is a warning rather than an error: a relation whose **Relation condition** makes the pairing unique still builds, and
the developer learns it may otherwise reach an arbitrary row out of several.

**A nullable unique target column.** The build refuses a relation pairing against a column that is both `[Unique]` and
nullable. This is settled and is not to be reopened. A nullable unique column matches at most one row but may match
none for reasons the relation cannot see, and it is the only case that would have needed a third `Key(…)` overload —
`Key(…)` keeps exactly two, matching types and a nullable left against a non-nullable right. It is a new refusal on a
shape that builds today, and it is deliberate.

**Tenancy across the pairs.** The cross-tenant warning becomes pair-based and direction-free: a **Tenancy column**
appearing on either side of the relation must be paired with a tenancy column on the other side, and a tenancy column
paired with nothing warns. This is stricter than the positional rule it replaces and it now covers the declaring side,
which the old rule missed. A conditioned relation whose pairs include the tenancy column on both sides produces no
warning — that is the shape the check exists to permit. A target whose whole primary key is the tenancy column is
reachable by pairing that one column plus a condition.

**A forgotten condition.** Where one table declares a relation with a condition and another with the same key pairs
and no condition, the unconditioned one silently returns every kind. That earns a warning.

### Diagnostics this step owns

| Id | Rule | Severity | Trigger |
| --- | --- | --- | --- |
| `PGSQL0031` | Relation to one row may reach several | Warning | The target-side columns contain nothing the target claims unique |
| `PGSQL0034` | Relation may resolve every kind | Warning | One table declares a relation with a condition and another with the same key pairs and no condition |
| `PGSQL0035` | Relation pairs against a nullable unique column | Error | A relation pairs against a target column marked `[Unique]` that is nullable |

Reshaped: `PGSQL0027` (relation could reach across tenants) keeps its id, title and Warning severity, and changes what
it looks at — both tables, pair by pair. Check that analyzer release tracking stays satisfied after the change.

`PGSQL0033` belongs to step 06 — do not take it here.

The uniqueness and tenancy checks read the pairs the resolver produced, so they apply to relations still declared in
the old attribute-argument form too. The tenancy check is stricter than the one it replaces, so some existing tenancy
generator tests will report differently; updating those expectations is part of this step, and it is the evidence the
new rule covers the direction the old one missed.

### Proving it end to end

Cover each rule in the generator harness with its companion "reports nothing" and "emitted source compiles"
assertions, including the case each warning exists to permit: a relation to one row pairing against a `[Unique]`
column, a conditioned relation pairing tenancy on both sides, and a superset of a unique set.

Then convert the tenant fixtures in the integration suite — the tenant project, tenant task and tenant link tables,
five declarations between them — to the new form, with their tests unchanged. They are the composite-key and
generated-column-per-kind shapes, so passing them unchanged is what shows pairs cover what **Key order** used to.

Add one fixture the suite does not have: a per-tenant singleton whose whole primary key is the tenancy column, reached
by pairing that one column plus a condition. Against a real container it must return the right tenant's single row and
must not warn.

### Boundaries

- Add the three new rows to `AnalyzerReleases.Unshipped.md` with their titles verbatim. Leave `README.md`, the
  library's `README.md`, `docs/adr/` and `Directory.Build.props` alone — step 07 owns them.
- The OData fixtures and the analyzer test sources stay on the old form; steps 05 and 06 move them. The old form must
  still resolve at the end of this step.

## Acceptance criteria

- [ ] A relation to one row pairing against the target's primary key, or against a `[Unique]` column, or against a
      superset of either, reports nothing; one pairing against nothing the target claims unique warns with
      `PGSQL0031` and still generates.
- [ ] A relation pairing against a `[Unique]` column that is nullable is an error, `PGSQL0035`, and drops only that
      relation. `Key(…)` still has exactly two overloads.
- [ ] `PGSQL0027` warns when a tenancy column on either side is paired with a non-tenancy column or with nothing at
      all, reports once per unpinned tenancy column, and drops nothing.
- [ ] A conditioned relation whose pairs include the tenancy column on both sides produces no `PGSQL0027`.
- [ ] `PGSQL0034` warns where a conditioned relation and an unconditioned one over the same key pairs are declared on
      one table.
- [ ] The tenant project, tenant task and tenant link fixtures in the integration suite are declared in the new form
      and their existing tests pass unchanged.
- [ ] A new integration fixture reaches a per-tenant singleton whose whole primary key is the tenancy column, through
      one pair plus a condition, and returns that tenant's row.
- [ ] Existing tenancy generator tests state the reshaped rule's behaviour rather than the positional rule's.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` are all green (Docker running).

## Outcome

`RelationResolver.cs` gained the three checks this step owns, and reshaped the tenancy check, all reading the
*resolved* key pairs rather than anything about how a relation was declared — so every check here applies equally to
the old attribute-argument form and the new `RelationDefinition<,>` form, per the step's own instruction.

- **`PGSQL0031` (warning, "Relation to one row may reach several").** `PairedColumnsClaimUniqueness` checks whether
  the target-side columns of a relation's resolved pairs contain the target's whole primary key, or any single
  `[Unique]` column — a superset of either passes. Only checked for a relation to one row (`!relation.IsToMany`); a
  relation to many is allowed to reach several by definition. It reads the pairs, not the condition, so a condition
  that happens to make an otherwise-non-unique pairing unique at run time does not suppress it — a claim, not a
  check, exactly like the spec says.
- **`PGSQL0035` (error, "Relation pairs against a nullable unique column").** The Settled section's decision, and the
  step file's instruction that this step is its natural home since it owns the uniqueness claim. Checked for every
  pair regardless of cardinality: if a pair's target-side property is both `[Unique]` and nullable, the relation is
  dropped — the same blast radius as every other relation-level error. `Key(…)` was not touched; it still has exactly
  the two overloads step 02 shipped. (One incidental finding while building the fixtures for this: the "nullable
  right side" shape the Settled section worried about for `[Unique]` columns already compiles today for an *ordinary*
  to-many relation's FK-nullable-on-the-target case, via the matching-types overload's generic inference unifying at
  the nullable type — e.g. `Key(x => x.ProjectId, y => y.ProjectRef)` where `ProjectId` is `long` and `ProjectRef` is
  `long?` infers `TValue = long?`. No third overload was needed for that case either; it was never actually blocked.)
- **`PGSQL0027` (reshaped, same id/title/severity), pair-based and direction-free.** `CheckTenancyPairing` now checks
  *both* tables' own tenancy columns against the pair that names each one, if any — `CheckTenancySide` is called once
  per side. A tenancy column whose pair's counterpart is not itself a tenancy column warns, and a tenancy column
  absent from every pair warns too. This is strictly stronger than the old rule, which only ever checked whichever
  side held the primary key (target for a relation to one row, declaring for a relation to many) — the declaring
  side's own tenancy column for a to-one relation, and the target's own tenancy column for a to-many relation, were
  never checked before. Reports once per unpinned tenancy column, so a relation touching neither side's tenancy
  column at all can now report twice for the same relation.
- **`PGSQL0034` (new, warning, "Relation may resolve every kind").** `CheckForForgottenConditions` runs once per
  table, before its relations resolve, and only over relations declared in the definition form (the old form cannot
  carry a condition, so it never participates). Relations targeting the same table with the identical set of
  `(declaringProperty, targetProperty)` pairs — compared as a set, so pair order carries no meaning, matching the
  spec — are grouped; where at least one relation in a group carries a condition, every relation in that group
  *without* one is reported. A pair with an unresolved side (already reported by `PGSQL0030`) is excluded from
  grouping rather than risking a false match on `null`.
- **Both declaration forms now build through one path.** `ResolvedRelation` lost its old
  `(foreignKeys, primaryKeys)` constructor; the old attribute-argument form's `TryResolve` now zips its
  `foreignKeys`/`primaryKeys` into `JoinedKeyPair`s itself (the same zip the removed constructor used to do
  internally) before calling the same `CheckKeyPairClaims` the definition form calls. This is why the uniqueness and
  tenancy checks apply to both forms without duplicating logic.

**Reshaping `PGSQL0027` changes what several existing generator tests assert**, as the step file warned it would:
`RelationToOneRow_PairedAgainstAnUnrelatedProperty_ReportsPGSQL0027_TheStrictForm` and
`RelationThatWarns_IsStillMirroredOntoTheDataTypeAndStillRegistered` in
`TableRepositoryGeneratorTenancyTests.cs` now assert **two** `PGSQL0027` diagnostics instead of one (the target's own
tenancy column unpinned, *and* the declaring table's own tenancy column unpinned — the direction the old rule
missed). `RelationToAnUntenantedTarget_ReportsNoWarning` no longer holds as written — a tenanted declaring table
whose relation to a shared, untenanted target does not pair the declaring table's own tenancy column now warns,
because that column is paired with nothing. It was split into two tests:
`RelationToAnUntenantedTarget_WhereNeitherTableDeclaresTenancy_ReportsNoWarning` (genuinely nothing to warn about on
either side) and `RelationToAnUntenantedTarget_FromATenantedDeclaringTable_ReportsPGSQL0027ForTheDeclaringSidesOwnTenancyColumn`
(states the new behaviour explicitly, with the reasoning in its own doc comment).

A new test class, `TableRepositoryGeneratorRelationKeyClaimsTests.cs` (18 tests), covers every rule this step owns:
`PGSQL0031` (against the primary key, against a `[Unique]` column, against a superset, warning-and-still-generates
for neither, a condition not suppressing it, and no warning for a relation to many), `PGSQL0035` (drops only that
relation; a non-nullable `[Unique]` column reports nothing), `PGSQL0034` (fires, compiles, and three "reports
nothing" companions — two conditioned relations sharing pairs, two unconditioned ones sharing pairs, and an
unconditioned one pairing *different* columns), and the two tenancy-permitting shapes the spec calls out by name: a
conditioned relation pairing the tenancy column on both sides, and the Settled section's per-tenant singleton (target
whole primary key is the tenancy column, reached by that one pair plus a condition) — both report no `PGSQL0027`.
Every new fact is backed by the "emitted source compiles" companion assertion per the testing decision.

The tenant project, tenant task and tenant link fixtures
(`test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/TenantProjectTable.cs`,
`TenantTaskTable.cs`, `TenantLinkTable.cs`) are converted to the `RelationDefinition<,>` form — five relations
total (`PrimaryTask`, `Tasks`, `Links` on the project side, `Project` on the task side, `Project` on the link side),
each a private nested class behind a private property per step 02's convention. `GeneratedRepositoryCompositeKeyTests.cs`
needed no change and passes unchanged (263/263 in the whole integration suite), which is the evidence pairs cover
what **Key order** used to. None of these three tables carry a tenancy column, so converting them exercises the
composite-key and generated-column-per-kind shapes without exercising `PGSQL0027` at all; the OData copies of
`TenantProjectTable`/`TenantTaskTable` were left on the old form, per this step's boundary.

The new integration fixture is `TenancyProfileTable` (`public.generated_tenancy_profiles`, added to `TestFixture.cs`):
its whole primary key is the single `[Column(Tenancy = true)] AccountId` column. `TenancyDocumentTable` gained a
private `Profile` relation reaching it by pairing that one column alone (`Key(x => x.AccountId, y => y.AccountId)`)
plus a condition (`(document, profile) => profile.IsActive`) — the shape the Settled section carves out, verified to
build with no diagnostic at all. Two new tests in `GeneratedRepositoryTenancyTests.cs`, against a real container,
confirm `Include(x => x.Profile)` reaches the caller's own tenant's active profile and reaches nothing when that
tenant's profile is inactive, proving the condition narrows materializing as well as the pairing narrows the join.

Verification, run sequentially in the foreground with Docker running:
- `dotnet format` — reformatted nothing beyond the files this step touched; `dotnet format --verify-no-changes`
  exits 0.
- `dotnet build` (whole solution) — 0 warnings, 0 errors.
- `dotnet test`, run per project (`DOTNET_ROLL_FORWARD=LatestMajor` for the net9.0 projects, the same pre-existing
  environment quirk steps 01–03 noted): Analyzers.Tests 165/165 (148 pre-existing + 18 new, minus one net change
  from the tenancy-test split above), Tests.Unit 197/197, Tests.Integration 263/263 (Docker/Testcontainers, 261
  pre-existing + 2 new), Tests.Integration.OData 134/134, Tests.Packaging 13/13. All green.

### Deviations

None from the spec. Two from the step file's own text, both in the direction of doing more than the letter asked,
not less:

1. The Settled section's refusal (`PGSQL0035`) was folded into this step exactly as instructed, but it is checked
   for *every* relation regardless of cardinality (to-one and to-many alike) rather than only to-one relations, since
   the spec's own wording — "a relation pairing against a column that is both `[Unique]` and nullable" — names no
   cardinality restriction, and a to-many relation's target-side pair can in principle name a `[Unique]` column too.
2. `PGSQL0027`'s reshape is checked as fully symmetric and direction-free — both tables' own tenancy columns are
   checked against the pairs, with no special-casing for cardinality — which is the literal reading of "a tenancy
   column appearing on either side ... must be paired with a tenancy column on the other side." This is stricter
   than the previous rule in a way the step file anticipated ("some existing tenancy generator tests will report
   differently") but did not fully spell out the shape of: a relation to a wholly untenanted target now still warns
   once, for the declaring table's own unpinned tenancy column, even though the old rule's rationale ("a relation to
   a shared, untenanted table can be legitimate") is still true of the *target* side. The existing test asserting
   the old, fully-permissive behaviour was split into two tests that each state one half of the new behaviour
   explicitly, rather than silently loosened or deleted.

`PGSQL0033` was not touched, per the step's boundary — it belongs to step 06. `README.md`, the library's `README.md`,
`docs/adr/` and `Directory.Build.props` were left alone, per the same boundary; `AnalyzerReleases.Unshipped.md`
gained the three rows this step owns (`PGSQL0031`, `PGSQL0034`, `PGSQL0035`).
