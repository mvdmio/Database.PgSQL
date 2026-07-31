---
status: accepted
---

# Let a column claim it carries the tenant, and make every generated member take its value

A multi-tenant consumer's tenancy guarantee lived at every call site. The application that drove
[ADR 0006](0006-composite-primary-keys.md) shapes fifty-three of its keyed tables as
`(account_id, <entity>_id)`, so a **Relation** whose foreign key includes `account_id` pins the target's tenant to
the source's inside the join — structural, and impossible to forget. The root of the query was not: `Query()` returned
every tenant's rows and only a hand-written `.Where(x => x.AccountId == accountId)` narrowed it. Forgetting that one
call is a cross-tenant leak that compiles, reviews like any other missing predicate, and returns plausible rows. The
**Table definition** already knew which column that was — declared, in the key, the same on every table — and said
nothing about it.

We decided that a **Table definition** names one or more **tenancy columns** through `[Column(Tenancy = true)]`; that
every generated member constrains every one of them; that a member which does not already constrain a tenancy column
takes its value as a parameter, and a generated command type carries it as a `required` property; and that the
guarantee stops at the edge of generated code.

## Considered options

**How far the guarantee reaches:**

- **Generated code only (chosen).** The failure being removed is a call site that looks finished and is not.
  `db.Linq.Query<TData>()` and hand-written Dapper do not look finished, so nobody arrives at them by forgetting, and
  what a consumer writes by hand is the consumer's to review.
- **Every route to an unscoped row.** Rejected. It needs an analyzer diagnostic on the **Query surface** the library
  itself exposes, and it still has no answer for hand-written SQL, so it would buy a partial guarantee at the cost of
  diagnosing a public API against its own documented use.

**The provider's entity-level query filter, and PostgreSQL row-level security.** Both rejected before the work started,
and both are what a future reader will reach for. linq2db's `HasQueryFilter` needs a tenant *value* at execution time,
and this library has no ambient per-request context of any kind — no `AsyncLocal`, no session or principal concept,
nothing on `DatabaseConnection` beyond host, port and database. Adopting it means introducing that state, and the
provider ships a public `IgnoreFilters` escape hatch, which makes the guarantee advisory. Row-level security is a
stronger guarantee than anything expressible in generated C#, but it is schema and session configuration: the library
has no `SET`/`set_config` support and migrations are hand-written.

**Whether the tenancy column must be part of the primary key:**

- **No requirement, with one uniform rule instead (chosen).** Every generated member constrains every tenancy column,
  and where the key already does it, that member is unchanged. Under the driving consumer's shape this generates
  exactly what a key-membership rule would generate, because `account_id` is the first member of every key. Under a
  surrogate key plus a tenant column — the common multi-tenant table elsewhere — it still works.
- **Required, refusing a tenancy column outside the key.** Rejected: it keeps `GetByPrimaryKeyAsync`, `UpdateAsync` and
  `DeleteByPrimaryKeyAsync` safe by construction and nothing else, and it refuses the shape most multi-tenant schemas
  actually have.

**How a command supplies the value:**

- **A `required` property on the generated command types (chosen).** Existing construction sites keep their
  object-initializer style and fail to compile until they supply the value, which is the same error a parameter would
  produce for a smaller change. It also keeps the tenant off the method signature on the two members whose value has
  somewhere better to live.
- **A parameter on `CreateAsync` and `UpdateAsync`.** Rejected: it splits where the value comes from — a parameter here,
  a property there — for no more safety.
- **A constructor argument.** Rejected: it rewrites every construction site, and a generated constructor can collide
  with one a consumer added to the partial class.

**Where the declaration lives:**

- **On `[Column]`, per table definition (chosen).** The road [ADR 0007](0007-declared-column-nullability.md) and
  [ADR 0008](0008-declared-column-storage.md) already took, so the three claims read alike and sit in one place.
- **Once for the assembly, with a per-table opt-out.** Rejected: not every table in a tenanted schema holds the column,
  and it would make a table tenanted by something outside the file that declares it, so a reader of one Table definition
  could not tell what its repository looks like.

## Consequences

- **The generated surface changes in one predictable way.** `Query()`, `GetAllAsync()`, `GetBy{Unique}Async` and
  `DeleteBy{Unique}Async` gain a parameter per tenancy column. `GetByPrimaryKeyAsync` and `DeleteByPrimaryKeyAsync` gain
  one only where the column is outside the key. `CreateAsync` and `UpdateAsync` gain none. Tenancy parameters come
  first, in declaration order. A member never takes a parameter for a column it already constrains, which is what stops
  a tenancy column carrying `[Unique]` from taking its own value twice.
- **A generated update can no longer move a row between tenants.** Where the tenancy column is outside the key, the
  `UPDATE` gains it in the `WHERE` and loses it from the `SET`. Without that, a caller holding a guessed key could
  reassign any row to their own tenant — the same hole as the missing root predicate, on the write side. An update aimed
  at another tenant's row now matches nothing and throws, exactly as one against a missing key already does. A consumer
  who genuinely needs to move a row writes the SQL by hand.
- **A table whose only assignable column was the tenancy column stops generating.** Dropping that column from the `SET`
  list can empty it, and the generator already refuses a table with nothing to update. That is the right answer — an
  update assigning no column does nothing — but the error names the table rather than the tenancy declaration, so it
  will read as unrelated to the line that caused it.
- **More than one tenancy column is permitted, because nothing breaks with two.** Each is constrained independently and
  every rule reads per column. Two-level tenancy — an account and a workspace — is the case that wants it, and refusing
  it would force such a consumer to pick which half of their guarantee to keep.
- **A relation can still reach across tenants, and the build warns rather than refuses.** `PGSQL0027` fires unless the
  foreign-key property paired against the target's tenancy column *is* the declaring table's own tenancy column — which
  also covers a declaring table that has no tenancy column at all. The loose form of that check, warning only when
  nothing is paired against the target's tenancy column, would pass a relation pairing some unrelated `Guid` against it,
  which is the reach-through the check exists to catch. It stays a warning because a relation-level problem drops the
  relation rather than the table under [ADR 0005](0005-table-relations-on-relation-properties.md), and because pairing
  against a shared table can be legitimate.
- **A malformed tenancy declaration abandons the table**, the way a malformed key does. `PGSQL0025` refuses a nullable
  tenancy column — a null tenant matches no row, so every generated member would return nothing — and `PGSQL0026`
  refuses one on a `[Generated]` column, which is on no command type and so has no property to require. Generating the
  table anyway would emit precisely the unguarded surface this decision exists to remove, and would do it quietly.
- **Nothing validates the value.** A caller passing another tenant's id is still a leak, and no signature prevents that.
  The library holds no tenant of its own and cannot tell a right value from a wrong one; it makes the value impossible
  to omit and nothing more. This is a smaller guarantee than it first reads as, and saying so is the point.
- **Generated code lands in the consumer's compilation, so `required` needs C# 11 there.** Every framework this package
  targets defaults above that. A consumer pinning `LangVersion` lower would not compile.
- **No existing consumer is affected.** A table naming no tenancy column generates what it generates today, so this is
  additive: MINOR under the project's pre-1.0 rule.
