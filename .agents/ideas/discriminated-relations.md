# Idea — A Relation that is chosen by a discriminator column

Status: wontfix — superseded by [relation-definitions](../specs/relation-definitions.md), which took the general form
of this idea. Kept for the reasoning below, in particular the PostgreSQL probe results.

## Motivation

A Relation today is fixed by the type of its Relation property. One property names one target Table definition, and its foreign-key properties are matched in Key order against that target's primary key. Two Relation properties may name the same foreign-key properties, but nothing tells them apart, so both resolve every row.

That leaves one common table shape undeclarable. A table can hold a pair of columns where one names a kind and the other holds an identifier, and the kind decides which table the identifier belongs to:

```
account_id  bigint
owner_id    uuid
target_kind text     -- 'Person', 'Asset', 'Incident', …
target_id   uuid
```

The shape a consumer wants to declare against it:

```
relation → people:  (account_id, target_id) where target_kind = 'Person'
relation → assets:  (account_id, target_id) where target_kind = 'Asset'
```

Rails calls this `belongs_to :polymorphic` and Hibernate calls it `@Any`. Entity Framework Core has no first-class support for it. This library has none either, and the pair is common enough that a consumer meeting it has to model its way around the library rather than with it.

## What it costs a consumer today

One consumer, a compliance application, has six tables of exactly that shape and three more where the pair sits on an ordinary entity row rather than a link table. Its kind column selects among roughly twenty-three tables.

Without a Relation that reads the discriminator, that consumer reaches its targets by adding a real column per kind — a stored generated column holding the identifier only when the kind matches — and declaring an ordinary Relation against each one. That is about ninety members: forty-five foreign-key properties and forty-five Relation properties, every one of them mirrored onto a generated data type, and from there onto whatever model a Query front-end builds out of it.

The columns themselves are not the library's fault and would not go away (see the next section). The ninety C# members would.

## What this is not

**It is not enforcement.** PostgreSQL cannot express a foreign key whose target table depends on another column's value. A consumer that wants the database to refuse a link pointing at a row that does not exist needs something physical per kind however this feature turns out. Probed against PostgreSQL 18:

| Probe | Result |
| --- | --- |
| Foreign key on a stored generated column | allowed |
| `on delete cascade` on one | allowed |
| `on delete restrict` on one | allowed |
| `on delete set null` on one | rejected — `invalid ON DELETE action for foreign key constraint containing generated column` |
| Adding the key while an orphan row exists | rejected until the orphans are cleared |

So the consumer keeps its per-kind generated columns for enforcement either way. This idea is about **traversal**, and about whether those columns also have to exist in C#.

**It is not a change to what a Relation claims.** A Relation is a claim about columns that already exist. Whether this feature keeps that position or starts emitting DDL for the per-kind column is an open question below, not a decision this doc makes.

## Goal

A Table definition can declare several Relations that resolve through the same foreign-key properties and separate on the value of a discriminator column. Filtering, ordering and materializing across one read exactly as they do across an ordinary Relation, and the consumer models no extra column to get them.

The word is free to use here. `CONTEXT.md` tells the reader not to call a Tenancy column a discriminator, which leaves the term unclaimed for the thing that actually is one.

## Open questions

These are the ones a grilling session should work through. They are listed, not answered.

**Declaring it**

- How is the discriminator named and its value stated? An argument on `[Relation]`, a second attribute, or something else.
- Must the discriminator be a mapped property on the Table definition, or may the attribute name a column the definition does not otherwise carry?
- The consumer's values are enum member names stored as text. Does the declaration take a typed value, a string, or both?
- Does the library validate that two Relations on one table do not claim the same discriminator value?

**Both directions**

- A link row reaching its target is the obvious direction. The other one — a target row reaching the links that point at it — needs the constant on the far side of the correspondence. Is that one feature or two?
- A Relation is one-directional today, and declaring one never implies the other. Does that hold here?

**Behaviour**

- Does `Include` work across one, and which cost does each direction carry? A Relation to one row folds into the statement; a Relation to many rows adds a statement per level.
- What happens when one query includes several discriminated Relations that share their foreign-key properties?
- Does the constant reach PostgreSQL as a parameter or as a literal, and what does that do to plan reuse?
- Nullability: the correspondence must not widen into an "or both are null" alternative, which would cost the composite index. A key member may not be nullable today for exactly this reason (`PGSQL0020`).

**Interactions**

- The Tenancy column. The consumer's foreign-key set leads with the account, so the discriminator predicate has to sit alongside the tenancy predicate rather than replace it, and `PGSQL0027` must not fire on a discriminated Relation that pairs the tenancy column correctly.
- A target keyed on the tenancy column alone. The consumer has per-account singletons whose whole primary key is `account_id`, so the discriminated Relation's foreign-key set is just that one column and the identifier column plays no part.
- A Query front-end. What does an OData `$filter` and `$expand` do across one, given that a `$expand` over a Relation to many rows already comes back empty unless `HandleNullPropagation` is turned off?
- Which build-time diagnostics this adds, in the style of `PGSQL0013`, `PGSQL0019` and `PGSQL0025`.

**Scope**

- Does the library emit the DDL for the per-kind generated column, or keep its position that a Relation creates nothing and verifies nothing? Emitting it would make the enforcement the consumer needs fall out of the same declaration. It would also be the first DDL a Table definition has ever produced.

## Notes

The consumer's side of this is tracked in the `mvdmio-suite` repository as `.agents/ideas/compliance-table-definitions-and-typed-links.md`, which is proceeding without this feature. It is not blocked on the outcome here — the feature would remove modelling cost from that work, not unblock it.
