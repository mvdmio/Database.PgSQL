# Table relations in the source generator

Status: needs-triage

## Motivation

A **Table definition** describes one table in isolation. It declares a table name, per-property column names, a primary key, unique columns, and generated columns — and nothing about how that table relates to any other. There is no foreign-key concept, no navigation property, and no way to say that one **Table definition** references another.

Everything downstream inherits that limitation. Generated repositories can only ever emit single-table SQL, and the query surface in [queryable generated repositories](../specs/queryable-generated-repositories.md) is single-table for exactly this reason: the provider it sits on expresses joins perfectly well, but the generator has no relation model to generate them from.

It surfaced because an OData endpoint over a generated repository cannot support `$expand` without one. `$expand` and navigation-property filtering are the same requirement wearing two names, and both need a relation model.

## Goal

Let a **Table definition** declare its relationships to other **Table definitions**, so that generated code can express cross-table queries — and so the single-table constraint on the query surface can be lifted deliberately rather than remaining an accident of what the generator happens to parse.

## Decisions (locked)

None. Nothing here has been decided; the two findings below are constraints any design has to accommodate, not choices already made.

- The provider's OData expand path requires enabling multiple-query mode, and then issues one query per expanded row set rather than a join. No design should assume `$expand` maps onto an efficient join for free.
- The applications that would consume this already join across tables and return **flat** data types — joining to a parent table purely to filter on it, then returning the child shape unnested. Flat-with-joins is the established idiom; nested expansion would be a new one, and it is worth establishing which is actually wanted before designing for the harder of the two.

## Out of scope

- Anything that changes the existing single-table query surface before a relation model exists.
- Mutation across relations — cascading writes, or creating a parent and child in one call.

## Open questions

- How is a relation declared — on a property, on the **Table definition** as a whole, or derived from column naming?
- Which cardinalities are supported — one-to-one, one-to-many, many-to-one, many-to-many — and how is each expressed?
- What does the generator emit for a relation: a navigation property on the data type, a separate join-aware query surface, or a flattened projection type?
- How are cycles handled? Two **Table definitions** referencing each other must not produce infinitely nested types or infinitely recursive SQL.
- Join or one-query-per-relation, and does the consumer get a say?
- How does a relation interact with the query surface's exception boundary, and what does the unmappable-property-type warning do across a relation?
- Does the Dapper-backed create, read, update and delete path gain anything from relations, or is this purely a query-surface concern?
