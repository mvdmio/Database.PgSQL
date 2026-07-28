# Declare a tenancy column once and let the compiler enforce it

Status: needs-triage

## Motivation

A multi-tenant consumer's tenancy guarantee currently lives at every call site. The
consumer that drove composite primary keys shapes all 53 of its keyed tables as
`(account_id, <entity>_id)`, so a **Relation** whose foreign key includes `account_id`
constrains the target's tenant to the source's inside the join — that part is
structural and cannot be forgotten. What is not structural is the root of the query:
`Query()` returns every tenant's rows, and only a hand-written `.Where(x => x.AccountId
== accountId)` narrows it. Forgetting that one call is a cross-tenant data leak that
type-checks, passes review as easily as any other missing predicate, and produces
plausible results.

The **Table definition** already knows which column that is. It is declared, it is part
of the primary key, and it is the same column on every table.

## Goal

Let a **Table definition** name its tenancy column once, and make omitting it at the
call site a compile error rather than a review responsibility — without the library
growing ambient per-request state.

## Decisions (locked)

None. Deliberately deferred out of the composite-primary-key work so that the keys
could land first; see [ADR 0006](../../docs/adr/0006-composite-primary-keys.md).

## Sketch that motivated filing this

A table declaring a tenancy column would generate `Query(Guid accountId)` in place of
`Query()`, emitting the root predicate itself, and `GetAllAsync(Guid accountId)`
likewise. Omission becomes a compile error, there is no ambient state to thread and no
bypass to audit, and the shape a **Query front-end** consumes is unchanged — an OData
controller does `repo.Query(accountId)` and hands the result to `ApplyTo` exactly as it
does today.

## Out of scope

- **The provider's own entity-level query filter.** linq2db 6.3.0 has `HasQueryFilter`,
  but a filter needs a tenant *value* at execution time and this library has no ambient
  per-request context of any kind — no `AsyncLocal`, no session or principal concept,
  nothing on `DatabaseConnection` beyond host, port and database. Adopting it means
  introducing that ambient state, and the provider also ships a public `IgnoreFilters`
  escape hatch, which makes the guarantee advisory rather than enforced.
- **PostgreSQL row-level security.** A stronger guarantee than anything expressible in
  generated C#, but it is a schema and session-configuration concern; the library has no
  `SET`/`set_config` support and migrations are hand-written.

## Open questions

- How is the tenancy column declared — a new attribute, or a marker on the existing
  `[PrimaryKey]`? Must it be part of the primary key, and must it be the first member?
- What happens to the Dapper surface? `GetByPrimaryKeyAsync` already takes every key
  member including the tenancy column, so it is safe by construction — but `GetAllAsync`
  and the `[Unique]` lookups are not.
- Does a relation reach-through need anything, or does the shared key column already
  carry the constraint in every case? It does when the foreign key includes the tenancy
  column, which is the shape that motivates this — but nothing forces a consumer to
  declare it that way.
- Can more than one table in a compilation declare a *different* tenancy column, and
  does a relation between two such tables mean anything?
- Is a required parameter enough, or does the value need validating against something?
  A caller passing the wrong tenant's id is still a leak, and no signature prevents that.
