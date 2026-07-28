# The mappable-type allowlist admits numeric types the driver cannot write

Status: needs-triage

## Motivation

The source generator's mappable-type allowlist (`QueryMappableTypes`) admits `sbyte`, `ushort`, `uint` and `ulong` as
property types on a table definition. A table definition using any of them compiles clean, generates a repository, and
registers a query mapping — and then cannot insert or update a single row. The PostgreSQL driver has no mapping for the
corresponding `DbType`s and refuses the parameter:

```
System.NotSupportedException: The DbType 'SByte' isn't supported by Npgsql.
There might be an Npgsql plugin with support for this DbType.
```

Reading works, so the failure is asymmetric and only shows up on the write path. It surfaces as a `QueryException` at
runtime, from generated code, with no build-time warning — which is the opposite of what the allowlist exists for. The
allowlist's whole job is to tell a consumer at build time which property types the library can carry.

Found while exercising the awkward-types entity in the OData conformance suite; pinned there by
`AwkwardTypeQueryTests.Parameter_OfAnAllowlistedTypeTheDriverRejects_ReportsTheUnsupportedDbType` so the behaviour cannot
change silently, but pinning it is not fixing it.

The neighbouring types are fine and worth stating so the boundary is clear: `char`, `byte`, `TimeSpan`, `byte[]` and
`DateTime` all round-trip.

## Goal

Make the allowlist's promise true: either the four types work on the write path, or the generator refuses them at build
time instead of at runtime.

## Decisions (locked)

None.

## Out of scope

- Anything about how these types map into an OData model. That is a separate question, answered by the conformance
  suite's `GeneratedTypeModelTests`, and it has different answers (`ulong` is lossy there, the others merely widen).

## Open questions

- Reject or support? Rejecting is a one-line allowlist change plus a diagnostic, and it is source-breaking for anyone
  who has such a property today — though anyone who does has a repository that throws on every write, so the break
  reveals a bug rather than causing one.
- If supporting: does a type handler per type suffice, or does the parameter type have to be set explicitly at the point
  the generated code builds its parameter dictionary? The driver rejects the `DbType` Dapper infers, so the fix probably
  belongs where the parameter is constructed rather than in a handler.
- Does the same hole exist for the bulk-copy path, which writes through the binary protocol rather than through
  parameters? That path may already work, which would make the inconsistency worse rather than better.
- Is `ulong` worth supporting at all, given no PostgreSQL integer type covers its range and the value has to land in
  `NUMERIC`?
