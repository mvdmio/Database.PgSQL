# Declare a tenancy column, and let the compiler enforce it

Status: ready-for-agent

## Problem Statement

A developer using this library on a multi-tenant database has to remember the tenant on every read.

A generated repository's `Query()` returns every tenant's rows. Only a hand-written
`.Where(x => x.AccountId == accountId)` narrows it, and `GetAllAsync()` has no way to narrow at all. Forgetting that
predicate compiles, passes review as easily as any other missing filter, and returns rows that look right. It is a
cross-tenant data leak with no symptom.

The same gap runs through the rest of the generated surface. A `[Unique]` lookup finds a row by one column and never
asks whose row it is. A `[Unique]` delete destroys it on the same terms. Where the tenancy column sits outside the
primary key, the generated update sets that column and addresses the row by key alone, so a caller holding a guessed
key can move any row into their own tenant.

The **Table definition** already knows which column carries the tenant. It is declared, it is often part of the primary
key, and it is usually the same column on every table in the schema. Nothing in the library asks it.

## Solution

A **Table definition** marks a column as a tenancy column, once, in the file that already declares it:

```csharp
[Column(Tenancy = true)]
[PrimaryKey]
public long AccountId { get; set; }
```

From then on the generated repository will not let a caller leave the tenant out. Every generated member constrains
that column. A member that does not already constrain it takes its value as a parameter, and the generated command
types carry it as a `required` property. Code that used to compile with the tenant missing now fails to build, and the
fix is to supply the value.

The guarantee is narrow on purpose, and worth stating plainly. It reaches generated code and stops there — the query
surface is still reachable directly through `db.Linq.Query<TData>()`, and hand-written Dapper is untouched. Nothing
checks the column against the real table, and nothing checks the value against anything, because the library holds no
tenant of its own and cannot tell a right value from a wrong one. It makes the value impossible to omit. It does not
make it impossible to get wrong.

See [ADR 0009](../../docs/adr/0009-declared-tenancy-column.md) for why it is shaped this way, and what was rejected —
in particular the query provider's own entity-level filter and PostgreSQL row-level security, which a future reader
will reach for.

## User Stories

1. As a developer with a multi-tenant table, I want to mark its tenancy column in the Table definition, so that the
   guarantee lives in one place instead of at every call site.
2. As a developer, I want `Query()` on a tenanted table to require the tenant, so that I cannot start a query over
   every tenant's rows by accident.
3. As a developer, I want the tenant predicate already applied to the queryable I get back, so that a query front-end
   composing on top of it cannot reach past it.
4. As a developer, I want `GetAllAsync` on a tenanted table to require the tenant, so that the one member with no
   predicate at all stops being the easiest leak in the surface.
5. As a developer, I want a `[Unique]` lookup on a tenanted table to require the tenant, so that finding a row by a
   business key cannot return another tenant's row.
6. As a developer, I want a `[Unique]` delete on a tenanted table to require the tenant, so that I cannot destroy
   another tenant's row by supplying a value I know.
7. As a developer whose tenancy column is part of the primary key, I want `GetByPrimaryKeyAsync` and
   `DeleteByPrimaryKeyAsync` to stay exactly as they are, so that a table already safe by construction gains no
   ceremony.
8. As a developer whose tenancy column sits outside the primary key, I want those two members to take the tenant as
   well, so that a row addressed by a surrogate key cannot be read or deleted across tenants.
9. As a developer, I want the generated create command to carry the tenancy column as a required property, so that a
   creation site that does not set it fails to build rather than writing a row under a default value.
10. As a developer, I want the generated update command to carry it the same way, so that both write paths read alike.
11. As a developer, I want a generated update to address its row by the tenancy column too, so that another tenant's
    row cannot be updated by a caller who guessed its key.
12. As a developer, I want a generated update never to assign the tenancy column, so that a row cannot change tenant
    through the generated surface.
13. As a developer who genuinely needs to move a row between tenants, I want that to remain possible through
    hand-written Dapper, so that a rare operation is not blocked outright.
14. As a developer with two-level tenancy — an account and a workspace, say — I want to mark both columns, so that I do
    not have to choose which half of my guarantee to keep.
15. As a developer with two tenancy columns, I want both constrained on every generated member, in the order I declared
    them, so that the parameter list is predictable from the file.
16. As a developer, I want tenancy parameters to come first on every signature, so that the surface reads the same way
    on every table.
17. As a developer whose tenancy column also carries `[Unique]`, I want its lookup to take that value once, so that the
    signature is not absurd.
18. As a developer, I want a relation that could reach across tenants to be flagged at build time, so that the one
    remaining hole in the guarantee is visible rather than silent.
19. As a developer, I want that flag to be a warning rather than an error, so that a legitimate relation to a shared
    table still builds.
20. As a developer who marks a nullable column as the tenancy column, I want the build to refuse it, so that I do not
    ship a repository whose every member returns nothing.
21. As a developer who marks a database-generated column as the tenancy column, I want the build to refuse it, so that
    I do not wait until run time to learn there was no property to require.
22. As a developer, I want a refused tenancy declaration to abandon the table rather than generate it unguarded, so
    that a mistake cannot quietly hand me back exactly the surface this feature removes.
23. As a developer with no tenancy column anywhere, I want my generated code to be byte-for-byte what it is today, so
    that upgrading costs me nothing.
24. As a developer reading someone else's Table definition, I want the tenancy column visible in that file, so that I
    can tell what the repository looks like without reading configuration elsewhere.
25. As a developer who depends on the generated repository interface rather than the class, I want it to change
    alongside the class, so that the guarantee holds wherever I inject it.
26. As a reviewer, I want a missing tenant to be a build error rather than something I have to spot, so that my
    attention goes to the questions a compiler cannot answer.
27. As a maintainer, I want the glossary to define what a tenancy column guarantees and what it does not, so that
    nobody mistakes it for row-level security.
28. As a maintainer, I want the README to state the same limits, so that a consumer forms the right expectation before
    they depend on it.

## Implementation Decisions

**The declaration.** `ColumnAttribute` gains a `bool Tenancy` property. Facts about a column live on `[Column]`, which
is the road [ADR 0007](../../docs/adr/0007-declared-column-nullability.md) and
[ADR 0008](../../docs/adr/0008-declared-column-storage.md) already took, so the three claims read alike and sit
together. There is no assembly-wide form and no configuration file: not every table in a tenanted schema holds the
column, and a table's generated shape must be readable from the file that declares it.

**More than one column may carry it.** They are constrained in declaration order, and every rule below reads per
column.

**The uniform rule.** Every generated member constrains every tenancy column. A member that already constrains one —
because the column is part of the primary key it addresses a row by, or because it *is* the unique column being looked
up — gains nothing. Every other member takes the value as a parameter, tenancy parameters first and in declaration
order, ahead of key parameters and ahead of a unique column's value.

Applied to the generated repository and its interface, both of which change together:

| Member | Tenancy column inside the primary key | Tenancy column outside it |
| --- | --- | --- |
| `Query` | gains a parameter | gains a parameter |
| `GetAllAsync` | gains a parameter | gains a parameter |
| `GetBy{Unique}Async` | gains a parameter * | gains a parameter * |
| `DeleteBy{Unique}Async` | gains a parameter * | gains a parameter * |
| `GetByPrimaryKeyAsync` | unchanged | gains a parameter |
| `DeleteByPrimaryKeyAsync` | unchanged | gains a parameter |
| `CreateAsync` | unchanged | unchanged |
| `UpdateAsync` | unchanged | unchanged |

\* Except for the tenancy column's own lookup and delete. Those already constrain it, so they take that value once.

`Query`'s existing optional `commandTimeout` stays last. `Query` applies the predicate itself and returns the narrowed
queryable, so a query front-end composes on top of a filter it cannot remove.

**The write path.** `CreateAsync` and `UpdateAsync` keep their single command parameter. The tenancy column becomes a
`required` property on both generated command types, which is a deliberate exception to the generator's standing rule
that `required` and `init` are never mirrored from a Table definition — this one is not mirrored, it is added. The
generated data type does not get it: that type is materialized by Dapper through a parameterless constructor, which
cannot satisfy `required`.

**The update statement.** Where the tenancy column sits outside the primary key, it joins the `WHERE` clause and leaves
the `SET` list. The set of columns an update assigns therefore becomes "not a key member, not generated, not a tenancy
column", and the predicate it addresses a row by becomes "every key member, plus every tenancy column not already among
them". The column stays on the update command type regardless, because the `WHERE` clause needs its value — what the
command carries and what the statement assigns are already two different sets in the generator, and this widens the gap
by one column. An update aimed at another tenant's row matches nothing and throws, exactly as an update against a
missing key already does.

One consequence falls out of that and should not be treated as a bug. The generator already refuses a table with
nothing to update. A table whose only assignable column was the tenancy column now has nothing left to assign, so it
becomes a build error where it used to generate. That is the right answer — an update that sets no column does
nothing — but the error names the table rather than the tenancy declaration, so it will read as unrelated.

**Everywhere else the column is ordinary.** It keeps its nullability claim, its storage claim, its place in the select
and returning lists, and its property on the generated data type. It is inserted by `CreateAsync` like any other column.

**Diagnostics.** Three are added.

- `PGSQL0025` refuses a nullable tenancy column. A null tenant matches no row, so every generated member would return
  nothing. This follows the existing rule refusing a nullable key member.
- `PGSQL0026` refuses `Tenancy = true` on a `[Generated]` column. Such a column is on no command type, so there is no
  property to make required.
- `PGSQL0027` warns on a relation that could reach across tenants. It fires unless the foreign-key property paired
  against the target's tenancy column is the declaring table's own tenancy column — which also covers a declaring table
  that has no tenancy column at all. For a relation to many the rule reads with the sides swapped, because the foreign
  key lives on the target: the check is that the target's tenancy column is what pairs against the declaring table's.
  One warning per unpaired tenancy column, naming the relation property.

The first two abandon the table, the way a malformed key does — generating it anyway would emit precisely the unguarded
surface this work removes. The third drops nothing, per
[ADR 0005](../../docs/adr/0005-table-relations-on-relation-properties.md).

**Compatibility.** A Table definition that names no tenancy column generates what it generates today. This is additive:
a MINOR bump under the project's pre-1.0 rule. One caveat belongs in the README — generated code lands in the
consumer's compilation, so `required` needs C# 11 there. Every framework this package targets defaults above that, but
a consumer pinning `LangVersion` lower will not compile.

**Documentation.** `CONTEXT.md` already carries the **Tenancy column** entry, written during the design session.
`README.md` gains the attribute, the table above, and the limits: generated code only, nothing verified, no validation
of the value.

## Testing Decisions

A good test here asserts what a consumer observes and nothing else. At the generator seam that is two things: the
diagnostics reported, and the source emitted. At the integration seam it is the rows a repository returns and the rows
left in the database. Neither kind should reach into how the generator decides anything.

**Seam one — `GeneratorHarness`.** The existing in-memory compilation harness every generator test already uses. It
gets `Tenancy` added to its `ColumnAttribute` stub. New tests belong in a `TableRepositoryGeneratorTenancyTests` class,
alongside the composite-key, nullability and storage classes that pin the other three claims the same way. Cover:

- Each row of the member table above, for a tenancy column inside the key and outside it.
- Parameter order: tenancy first, then keys, then a unique value, with `commandTimeout` still last on `Query`.
- The `required` property on both command types, and its absence from the data type.
- Two tenancy columns on one table, constrained in declaration order.
- A tenancy column that also carries `[Unique]`, taking its value once.
- The update statement: the tenancy column in the `WHERE` and not in the `SET`, only where it is outside the key.
- `PGSQL0025`, `PGSQL0026` and `PGSQL0027`, each fired and each not fired, including the strict form of the relation
  check — a relation pairing an unrelated property against the target's tenancy column must warn.
- A table declaring no tenancy column emits exactly what it emits today.

**Seam two — the integration project's `GeneratedRepositories` folder.** Real Table definitions against a real
PostgreSQL container, which is where `GeneratedRepositoryCompositeKeyTests` and `GeneratedRepositoryNullabilityTests`
already pin behaviour. Add a new table set rather than marking the existing `TenantProject`/`TenantTask`/`TenantLink`
tables, whose current generated shape those composite-key tests depend on. The new set needs one table with the
tenancy column inside the primary key and one with it outside, each with a `[Unique]` column, plus migrations and the
matching schema file update. Cover:

- Two tenants' rows present; `Query` and `GetAllAsync` return only the caller's.
- A `[Unique]` lookup for a value belonging to another tenant returns null, and the corresponding delete leaves that
  row in place.
- `GetByPrimaryKeyAsync` with the wrong tenant returns null where the tenancy column is outside the key.
- An update aimed at another tenant's row changes nothing and throws.
- An update of the caller's own row leaves the tenancy column as it was.
- A create writes the row under the tenant the required property carries.

`PGSQL0027` is not exercised at the integration seam: a warning in that project would be a build warning on every run.

## Out of Scope

- **Closing the direct query surface.** `db.Linq.Query<TData>()` stays as it is, and no analyzer diagnoses its use.
  The failure this work removes is a call site that looks finished and is not; a direct call does not look finished.
- **Hand-written Dapper.** Untouched, and the escape hatch for anything the generated surface now refuses — moving a
  row between tenants, or reading across tenants deliberately.
- **The query provider's entity-level filter, and PostgreSQL row-level security.** Both considered and rejected in
  [ADR 0009](../../docs/adr/0009-declared-tenancy-column.md). Neither is revisited here.
- **Validating the tenant value.** Nothing checks it. A caller passing another tenant's identifier is still a leak, and
  no signature prevents that.
- **Verifying the column against the real table.** Nothing does, in keeping with every other claim the library takes on
  trust.
- **The bulk copy and upsert paths.** They address a table by name and generate nothing, so there is no signature to
  change.
- **Any generated member gaining a deliberate cross-tenant form.** No `QueryAllTenants` and no equivalent.

## Further Notes

The idea file this came from is `.agents/ideas/declared-tenancy-column.md`. Every open question it listed is answered,
either here or in ADR 0009; it can be deleted when this ships.

The narrowest reading of the guarantee is the correct one, and it is worth repeating because the feature is easy to
oversell. Marking a column makes the tenant impossible to omit on generated code. It does not authenticate anyone, does
not remember who is calling, and does not stop a caller from passing the wrong value. A consumer who needs a guarantee
that survives a wrong value needs row-level security, which is a schema and session concern this library does not
reach.
