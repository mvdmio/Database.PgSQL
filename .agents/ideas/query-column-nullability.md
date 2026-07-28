# Carry column nullability into generated query mappings

Status: specced — see [.agents/specs/query-column-nullability.md](../specs/query-column-nullability.md)

## Motivation

Generated mappings tell the **Query surface** a column's name and whether it is the primary key, but nothing about whether it can hold null. The provider therefore assumes every column is nullable.

That assumption has a cost, because the provider's null-comparison mode — kept deliberately, per [ADR 0004](../../docs/adr/0004-linq2db-as-the-queryable-provider.md), because it matches both C# and the OData specification — compensates for possible nulls by widening every inequality with an "or the column is null" alternative. On a column that genuinely cannot be null, that alternative is dead weight: it can never match, and it makes the predicate non-sargable, so PostgreSQL cannot use an index that would otherwise serve the query.

A **Table definition** already knows the answer. Its properties are either nullable or not, and the generator already reads their types closely enough to warn about ones the query surface cannot map at all.

## Goal

Let the generated mapping state a column's nullability, so that predicates over non-nullable columns reach PostgreSQL without a null alternative that can never be true — without changing the null-comparison semantics that ADR 0004 locked in for columns that really are nullable.

## Decisions (locked)

None.

- One constraint any design has to accommodate: the mapping is described through a builder that is part of the library's public surface, because generated code in a consumer's assembly has to call it. Adding nullability to it is a public API change, not an internal one, so it carries a version bump and a support commitment.

## Out of scope

- Changing the null-comparison mode itself. That is settled and it is the specification-correct choice.
- Inferring nullability from the database rather than from the **Table definition**. The definition is the single source every generated type derives from and that should not change here.

## Open questions

- Is a property's nullable annotation trustworthy enough to drive SQL generation, given a consumer may have annotations disabled?
- What happens when the **Table definition** and the actual table disagree — a property declared non-nullable over a column that permits null? Silently wrong results are the risk, and the failure is invisible until a null appears.
- Does this belong on the mapping builder, or is it better derived from the definition at generation time so the public surface does not change?
- ~~Is the sargability win measurable at realistic table sizes, or is this theoretical? Worth measuring before designing.~~ **Measured, and it is not theoretical.** Established against PostgreSQL 18 on two 50k-row tables joined on a two-column key with a matching btree index on both sides, comparing the widened form against the plain equality it would become. The composite index degrades to leading-column-only: the second column moves out of `Index Cond` into `Filter`, index searches rise from 250 to 50,000, shared buffers from 1,516 to 25,116,417, and runtime 232x. On the default planner's hash join the second column is demoted to a `Join Filter` that removes 24,975,000 rows, costing 54x; on a selective 500-row driving set, 18x. Row counts are identical in every case, so the widening is pure cost wherever the column cannot actually hold null. The measurement was taken on a join `ON` clause rather than a `WHERE` predicate, but both come from the same widening.
- Do the unique and generated column markers imply anything about nullability that could be reused?
