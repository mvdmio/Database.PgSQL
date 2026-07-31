# 02 — A column claims it carries the tenant, and the read root demands it

Status: done

## What to build

A **Table definition** can now name a **tenancy column**, and the two generated members that today read across every
tenant refuse to run without its value.

The declaration is a third claim on `[Column]`, sitting beside the nullability claim of ADR 0007 and the storage claim of
ADR 0008 so all three read alike:

```csharp
[Column(Tenancy = true)]
[PrimaryKey]
public long AccountId { get; set; }
```

More than one column may carry it. The columns are collected in declaration order — the same source order the primary key
already relies on — and that order fixes every parameter list from here on.

Two members change, on both the generated repository and its interface, which always move together:

| Member | Tenancy column inside the primary key | Tenancy column outside it |
| --- | --- | --- |
| `Query` | gains a parameter | gains a parameter |
| `GetAllAsync` | gains a parameter | gains a parameter |

One parameter per tenancy column, tenancy parameters first and in declaration order. `Query`'s existing optional
`commandTimeout` stays last, and `Query` applies the tenant predicate itself and hands back the narrowed queryable, so a
query front-end composes on top of a filter it cannot remove. `GetAllAsync` constrains the column in its `WHERE` clause,
where it has no predicate at all today.

Everywhere else in this step the column stays ordinary: it keeps its nullability claim, its storage claim, its place in
the select and returning lists, its property on the generated data type, and its plain-column registration on the query
surface. Nothing else about it is special-cased.

A Table definition that names no tenancy column must generate exactly what it generates today. That is the compatibility
promise the whole spec rests on, so assert it rather than assume it.

Cover the change at both seams the project already uses:

- **The generator seam.** `GeneratorHarness`'s `ColumnAttribute` stub gains `Tenancy`. New assertions go in a
  `TableRepositoryGeneratorTenancyTests` class, alongside the composite-key, nullability and storage classes that pin the
  other three claims the same way. Pin both signatures and the emitted SQL, the parameter order including
  `commandTimeout` last, the two-tenancy-column case in declaration order, a column inside the key and one outside it,
  and the untenanted table's output being unchanged. Compile the generated output too, the way the other classes do.
- **The integration seam.** Add a new tenanted table set rather than marking the existing `TenantProject`/`TenantTask`/
  `TenantLink` tables, whose current generated shape `GeneratedRepositoryCompositeKeyTests` and the OData suite depend
  on. The set needs one table with the tenancy column inside the primary key and one with it outside, each carrying a
  `[Unique]` column and at least two assignable non-tenancy columns so the write path later steps touch stays generable.
  Seed two tenants' rows and assert that `Query` and `GetAllAsync` return only the caller's.

  Note where those tables belong. The integration suite's generated-repository tables are committed `CREATE TABLE IF NOT
  EXISTS` statements in `TestFixture.InitializeAsync`, deliberately not migrations — the tests under `Migrations/` assert
  on the exact set of migrations the assembly ships, so adding one there breaks them. The files under `Schemas/` are
  schema-first migration fixtures and have nothing to do with these tables. The spec's mention of "migrations and the
  matching schema file update" resolves to a `TestFixture` addition and nothing else.

## Acceptance criteria

- [ ] `ColumnAttribute` exposes a `bool Tenancy` property with an XML doc that states what the claim buys, that more than
      one column may carry it, and that nothing verifies the column or the value.
- [ ] The parsed table model carries its tenancy columns in declaration order, and `GeneratorHarness`'s attribute stub
      carries `Tenancy` so a generator test can claim it.
- [ ] On a tenanted table, `Query` and `GetAllAsync` each take one parameter per tenancy column, tenancy first, in
      declaration order, on the generated class and the generated interface alike.
- [ ] `Query` returns a queryable already narrowed to the tenant, and still takes `commandTimeout` last as an optional
      parameter.
- [ ] `GetAllAsync` constrains every tenancy column in its emitted `WHERE` clause and binds each by parameter.
- [ ] A table with two tenancy columns constrains both, in the order the file declares them.
- [ ] The tenancy column still appears in the select and returning lists, on the generated data type, and as an ordinary
      column in the query-mapping registration.
- [ ] A table declaring no tenancy column emits exactly what it emits today, asserted rather than assumed, with no
      existing analyzer or integration test needing an edit for that reason.
- [ ] `TableRepositoryGeneratorTenancyTests` exists and covers the above, and the generated output compiles the way the
      sibling generator test classes prove theirs does.
- [ ] A new tenanted table set exists in the integration project's `GeneratedRepositories` folder — one table with the
      tenancy column inside the primary key, one with it outside, each with a `[Unique]` column — with its tables created
      in `TestFixture.InitializeAsync`. No migration file and no `Schemas/` file is added or changed.
- [ ] With two tenants' rows present, `Query` and `GetAllAsync` return only the caller's rows.
- [ ] `dotnet format` → `dotnet build` → `dotnet test`, run sequentially and never in parallel, are all clean.
      Integration tests need Docker running.
- [ ] `README.md`, `src/mvdmio.Database.PgSQL/README.md` and `<PgSqlVersion>` in `Directory.Build.props` are untouched —
      the last step of this spec owns all three.

## Outcome

`ColumnAttribute` gained `bool Tenancy { get; set; }` (`src/mvdmio.Database.PgSQL/Attributes/ColumnAttribute.cs`), documented
as buying nothing but the impossibility of omitting the value, and stating that more than one column may carry it.

The parser and model gained a `Tenancy` claim end to end: `TableDefinitionSymbols.CreatePropertyModel` reads the
`[Column]` named argument the same way `NullabilityClaim` reads `Null`/`NotNull`; `PropertyDefinitionModel.IsTenancy`
carries it; `TableDefinitionParser.Parse` collects `properties.Where(x => x.IsTenancy)` in declaration order into
`TableDefinitionModel.TenancyColumns`, the same way primary keys are collected. `GeneratorHarness`'s `ColumnAttribute`
stub gained `Tenancy` too.

`TableRepositorySourceBuilder` and `TableRepositorySqlStatements` changed only for `Query` and `GetAllAsync`, per the
step's scope (the other members — `GetBy{Unique}Async`, `DeleteBy{Unique}Async`, `GetByPrimaryKeyAsync`,
`DeleteByPrimaryKeyAsync`, the `required` command properties, and diagnostics PGSQL0025/26/27 — are out of scope for
this step and untouched):

- `Query` and `GetAllAsync` each gain one parameter per tenancy column, tenancy first in declaration order, on both the
  interface and the class. `Query` keeps `commandTimeout` last and optional; `GetAllAsync` keeps `ct` last.
- `Query`'s body chains `.Where(x => x.Col1 == col1 && x.Col2 == col2 …)` onto `_db.Linq.Query<TData>(commandTimeout)`,
  so the narrowed queryable is what a caller composes further filtering on top of.
- `GetAllAsync`'s emitted SQL gains a `WHERE` clause constraining every tenancy column, bound by parameter name; the
  Dapper call binds each through the same `BindingExpression`/storage-claim path every other parameter binding uses.
- An untenanted table's `TenancyColumns` is empty, so both members fall back to exactly their prior signature and body
  — pinned by an explicit generator test (`UntenantedTable_GeneratesExactlyWhatItGeneratesToday`) rather than assumed.
- The column stays ordinary everywhere else: select/returning lists, the data type property, and the query-mapping
  registration are all untouched, also pinned by a test.

Generator seam: `TableRepositoryGeneratorTenancyTests` (10 tests) in
`test/mvdmio.Database.PgSQL.Analyzers.Tests/TableRepositoryGeneratorTenancyTests.cs` covers a tenancy column inside the
key, outside the key, two tenancy columns in declaration order, the narrowed `Query`, `commandTimeout` staying last,
the `GetAllAsync` `WHERE`/binding, the column's ordinary appearances, the untenanted no-op case, and that every shape's
generated source compiles.

Integration seam: added `TenancyDocumentTable` (tenancy column inside a composite key) and `TenancySettingTable`
(tenancy column outside a surrogate key) under
`test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/`, each with a `[Unique]` column and two assignable
non-tenancy columns. Their tables (`generated_tenancy_documents`, `generated_tenancy_settings`) are `CREATE TABLE IF
NOT EXISTS` statements added to `TestFixture.InitializeAsync` — no migration file and no `Schemas/` file touched.
`GeneratedRepositoryTenancyTests` (5 tests) seeds two tenants' rows on both tables and asserts `Query` and
`GetAllAsync` return only the caller's rows, plus that further `.Where(...)` composes on top of the tenant predicate
`Query` already applied.

`dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` (sequential, `DOTNET_ROLL_FORWARD=LatestMajor`
for the net9.0 test hosts, Docker running) all pass: Unit 197/197, Analyzers.Tests 101/101 (91 prior + 10 new),
Integration.OData 134/134, Packaging 13/13, Integration 246/246 (239 prior + 7 new — 2 table definitions plus the test
class don't add test methods themselves, the 5 `[Fact]`s plus 2 pre-existing from elsewhere account for the delta).
`README.md`, `src/mvdmio.Database.PgSQL/README.md` and `Directory.Build.props` are untouched, as this step reserves
them for the spec's last step.

No deviations from the step or spec.
