# Verify Table definitions against the real schema

Status: needs-triage

## Motivation

A **Table definition** is taken at its word about everything. Its table name, its column names, its composite key, which
columns the database generates, and — since
[ADR 0007](../../docs/adr/0007-declared-column-nullability.md) — whether a column can hold null are all claims the
library acts on without checking. Nothing emits DDL and migrations stay hand-written, so a definition and the table it
describes can drift apart silently.

Most of those claims fail loudly and immediately: a wrong column name is an error on the first query. The **nullability
claim** is the one worth thinking about, because it fails only when a row containing the offending null is read — which
may be long after deploy, and in production rather than in a test.

The database already knows all of it, and this repo already reads it: `SchemaCatalogReader` pulls columns with their
nullability for schema export.

## Goal

Let a developer find out that a definition disagrees with the database, at a moment of their choosing rather than when a
query happens to hit the disagreement.

## Decisions (locked)

None.

## Open questions

- Where does this live? The `db` tool has a schema-pulling path and a config file already, so a `db verify` sits
  naturally there — but the tool would then need to load the consumer's assemblies to find the definitions, which it
  currently does only for migrations.
- Or is it better as something a consumer's own integration test calls, given the library already has the reader and the
  definitions are in the consumer's assembly?
- Which disagreements are errors and which are informational? A definition claiming not-null over a nullable column is
  dangerous; the reverse merely leaves performance on the table.
- Does it check the whole schema or only the tables that have definitions? Extra columns in the database are normal and
  not a fault.
