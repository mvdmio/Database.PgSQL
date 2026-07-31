# 03 — Every read and delete that addresses a single row asks whose row it is

Status: done

## What to build

The uniform rule reaches the rest of the read surface and the deletes. Every generated member constrains every tenancy
column; a member that already constrains one gains nothing, and every other member takes its value as a parameter:

| Member | Tenancy column inside the primary key | Tenancy column outside it |
| --- | --- | --- |
| `GetBy{Unique}Async` | gains a parameter * | gains a parameter * |
| `DeleteBy{Unique}Async` | gains a parameter * | gains a parameter * |
| `GetByPrimaryKeyAsync` | unchanged | gains a parameter |
| `DeleteByPrimaryKeyAsync` | unchanged | gains a parameter |

\* Except the tenancy column's own lookup and delete. A tenancy column that also carries `[Unique]` is already
constrained by the value being looked up, so `GetBy{Tenant}Async` takes that value once rather than twice — otherwise the
signature is absurd.

The two key-addressed members are where the inside/outside distinction pays. Where the tenancy column is part of the
primary key the row is already tenant-scoped by construction, and a table that was safe gains no ceremony. Where the
column sits outside the key, a caller holding a guessed surrogate key could read or destroy any row, so the column joins
the predicate and its value joins the signature.

Parameter order is the same on every member: tenancy parameters first in declaration order, then key parameters, then a
unique column's value. Repository class and interface change together.

A `[Unique]` delete that names another tenant's value must now match nothing rather than destroy the row.

Cover it at both seams:

- **The generator seam.** Extend `TableRepositoryGeneratorTenancyTests` with each row of the table above, for a tenancy
  column inside the key and outside it; the parameter order on every signature; the tenancy column that also carries
  `[Unique]` taking its value once; and the emitted `WHERE` clauses. Keep asserting that the generated output compiles.
- **The integration seam.** Over the tenanted table set added in step 02: a `[Unique]` lookup for a value belonging to
  another tenant returns null; the matching delete leaves that row in place; and `GetByPrimaryKeyAsync` with the wrong
  tenant returns null where the tenancy column is outside the key, while the inside-the-key table's signature is
  unchanged.

## Acceptance criteria

- [ ] `GetBy{Unique}Async` and `DeleteBy{Unique}Async` take one parameter per tenancy column, tenancy first in
      declaration order, then the unique value, and constrain every tenancy column in the emitted statement.
- [ ] Where a tenancy column also carries `[Unique]`, its own lookup and delete take that value exactly once.
- [ ] `GetByPrimaryKeyAsync` and `DeleteByPrimaryKeyAsync` are byte-for-byte unchanged where every tenancy column is a key
      member.
- [ ] Where a tenancy column sits outside the primary key, those two members take it as a parameter ahead of the key
      parameters and constrain it in the emitted `WHERE` clause alongside every key member.
- [ ] The generated interface and the generated class carry the same signatures.
- [ ] A table declaring no tenancy column still emits exactly what it emits today.
- [ ] The generator tests cover each row of the member table for both placements of the column, and the generated output
      compiles.
- [ ] Integration tests show a cross-tenant `[Unique]` lookup returning null, the matching cross-tenant delete leaving the
      row in place, and `GetByPrimaryKeyAsync` returning null for the wrong tenant where the column is outside the key.
- [ ] `dotnet format` → `dotnet build` → `dotnet test`, run sequentially and never in parallel, are all clean.
      Integration tests need Docker running.
- [ ] `README.md`, `src/mvdmio.Database.PgSQL/README.md` and `<PgSqlVersion>` in `Directory.Build.props` are untouched —
      the last step of this spec owns all three.

## Outcome

Extended the tenancy rule from `Query`/`GetAllAsync` (step 02) to the rest of the key-addressed and unique-addressed
read/delete surface, in both `TableRepositorySqlStatements.cs` (SQL text) and `TableRepositorySourceBuilder.cs`
(C# emission), per the split step 01 made:

- **`GetByPrimaryKeyAsync`/`DeleteByPrimaryKeyAsync`.** New `TableRepositorySqlStatements.BuildKeyAndTenancyPredicate`
  constrains every primary-key member plus every tenancy column *not already a key member* (`TenancyColumnsOutsideKey`
  helper, mirrored privately in both files since each already recomputes what it needs — same precedent
  `BuildKeyPredicate`/`KeyParameterList` set). Left the original `BuildKeyPredicate` untouched, since `BuildUpdateSql`
  still calls it and the update path is out of this step's scope (later steps). New
  `TableRepositorySourceBuilder.KeyAndTenancyParameterList` prefixes `KeyParameterList` with the same outside-key
  tenancy columns, tenancy first. Where every tenancy column is already a key member, both the predicate and the
  parameter list come out byte-for-byte identical to before — pinned by an explicit generator test.
- **`GetBy{Unique}Async`/`DeleteBy{Unique}Async`.** New `TableRepositorySqlStatements.BuildLookupPredicate` and
  `TableRepositorySourceBuilder.LookupParameterList`/`TenancyColumnsExcept` constrain the unique column plus every
  tenancy column *other than the property itself* — so a tenancy column that also carries `[Unique]` takes its value
  once, not twice, on both the predicate and the parameter list.
- Parameter order everywhere follows the spec: tenancy parameters first in declaration order, then key parameters or
  the unique value. The emitted `WHERE` predicates put the key/unique predicate first and the extra tenancy predicates
  after, joined by `AND` — a stylistic choice (the spec doesn't mandate SQL text order), pinned by generator tests.
  Dapper parameter dictionaries follow the same order as the predicate they serve, for readability.
- `CreateAsync`/`UpdateAsync`, the `required` command properties, and diagnostics PGSQL0025/26/27 are untouched —
  out of scope per the step's member table and the spec's full table, reserved for later steps (04-07).

Generator seam: extended `TableRepositoryGeneratorTenancyTests`
(`test/mvdmio.Database.PgSQL.Analyzers.Tests/TableRepositoryGeneratorTenancyTests.cs`, now 19 tests, +9) with a new
`TENANCY_COLUMN_IS_UNIQUE` table constant, a `[Unique] Code` property added to `TENANCY_OUTSIDE_KEY` and
`TWO_TENANCY_COLUMNS` so every shape has a lookup to exercise, and new tests covering: `GetByPrimaryKeyAsync`/
`DeleteByPrimaryKeyAsync` unchanged when tenancy is inside the key, gaining a parameter when outside, and both
tenancy columns constrained when both sit outside; `GetBy{Unique}Async`/`DeleteBy{Unique}Async` gaining a tenancy
parameter for both key placements and for two tenancy columns in declaration order; the tenancy-column-is-unique case
taking its value once on both the lookup and the primary-key methods; and an untenanted table with a `[Unique]`
column emitting exactly what it emits today. All new shapes assert the generated source compiles and reports no
diagnostics.

Integration seam: extended `GeneratedRepositoryTenancyTests`
(`test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/GeneratedRepositoryTenancyTests.cs`, +6 tests)
over the existing `TenancyDocumentTable` (tenancy inside the key) and `TenancySettingTable` (tenancy outside a
surrogate key) from step 02 — no new tables needed, both already carry a `[Unique] Code` column. New tests: a
cross-tenant `GetByCodeAsync` returns null and the matching `DeleteByCodeAsync` leaves the row in place, for both
tables; `GetByPrimaryKeyAsync` with the wrong tenant returns null where the tenancy column is outside the key; and
`GetByPrimaryKeyAsync`'s signature is confirmed unchanged (single account parameter, since it's already part of the
key) where the tenancy column is a key member.

`dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` (sequential, `DOTNET_ROLL_FORWARD=LatestMajor`
for the net9.0 test hosts, Docker running) all pass: Unit 197/197, Analyzers.Tests 110/110 (101 prior + 9 new),
Integration.OData 134/134, Packaging 13/13, Integration 252/252 (246 prior + 6 new). `README.md`,
`src/mvdmio.Database.PgSQL/README.md` and `Directory.Build.props` are untouched, reserved for the spec's last step.

No deviations from the step or spec.
