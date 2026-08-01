# 04 — What the pairs have to claim: uniqueness and tenancy

Status: pending

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
