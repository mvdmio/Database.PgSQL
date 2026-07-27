# Queryable generated repositories

Status: ready-for-agent

## Problem Statement

Consumers of `mvdmio.Database.PgSQL` who declare a **Table definition** get a generated repository with a fixed set of methods: create, get-all, get-by-primary-key, get-by-unique-column, update, and delete-by. Every query is a SQL string literal baked in at generation time.

That surface cannot express a query whose shape is only known at runtime. An application that needs to filter on a column chosen by the caller, sort by a caller-supplied field, or page through a caller-supplied window has to abandon the generated repository and hand-write SQL — which is what the generated repository existed to avoid.

Concretely, a consuming application in `mvdmio-suite` is building an OData endpoint. `[EnableQuery]` composes `$filter`, `$orderby`, `$top` and `$skip` onto an `IQueryable<T>` that the action returns. There is nothing in this library that can produce one. OData is the immediate driver, but not the only one: several candidate approaches for runtime-composed queries in that application converge on `IQueryable<T>` as the interchange type, so the requirement is the type itself, not merely the capability of composing filters.

## Solution

Generated repositories gain a `Query()` method returning `IQueryable<{Entity}Data>` — a deferred, composable query over the **Table definition**'s table, backed by [linq2db](https://linq2db.github.io/) as the LINQ provider.

`DatabaseConnection` gains a `Linq` adapter alongside the existing `Dapper` and `Bulk` adapters. It owns the linq2db context, binds it to the connection and to any ambient transaction, and holds the mapping between generated data types and their tables. Consumers use `Query()`; the adapter is the plumbing beneath it plus a hook for registering custom type conversions.

The library stays free of any dependency on OData or on any other query front-end. It produces a standard `IQueryable<T>`; what a consumer composes onto it, and how they constrain it, is the consumer's business.

Existing repository methods are untouched and continue to run on Dapper.

## User Stories

1. As an application developer, I want a `Query()` method on my generated repository, so that I can build a query whose shape is decided at runtime instead of at code-generation time.
2. As an application developer, I want `Query()` to return the standard `IQueryable<T>`, so that I can hand it to any framework or library that consumes queryables.
3. As an application developer building an OData endpoint, I want to return the result of `Query()` from an `[EnableQuery]` action, so that `$filter`, `$orderby`, `$top` and `$skip` translate to SQL instead of loading the table into memory.
4. As an application developer, I want `Query()` to appear on the generated repository interface as well as the class, so that I can substitute a fake in tests.
5. As an application developer, I want filtering by equality, inequality, ordering comparison, and boolean combination to translate to SQL, so that the database does the work.
6. As an application developer, I want `OrderBy` and `ThenBy` in both directions to translate to SQL, so that sorting happens in the database.
7. As an application developer, I want `Skip` and `Take` to translate to SQL, so that paging does not transfer rows I discard.
8. As an application developer, I want `Count`, `LongCount` and `Any` to translate to SQL, so that I can answer existence and cardinality questions without materializing rows.
9. As an application developer, I want `First`, `FirstOrDefault`, `Single` and `SingleOrDefault` to work, so that single-row lookups on non-indexed criteria are expressible.
10. As an application developer, I want a query composed from a runtime-supplied local variable to become a SQL parameter rather than an inlined literal, so that PostgreSQL can reuse query plans.
11. As an application developer, I want `x.Column != value` to return rows where the column is null, so that the query behaves the way the C# I wrote says it behaves.
12. As an application developer, I want an awaitable way to materialize a query, so that I do not block a request thread inside an ASP.NET application.
13. As an application developer, I want the async materialization methods to live in this library's namespace, so that I do not have to import a third-party namespace to await a query I got from this library's API.
14. As an application developer, I want the queryable to be consumable as an `IAsyncEnumerable<T>`, so that frameworks which detect async enumeration use it without knowing about this library.
15. As an application developer, I want a query composed inside an open transaction to see rows written earlier in that same transaction, so that read-your-own-writes holds.
16. As an application developer, I want a query composed before a transaction begins and enumerated after, to run against the transaction that is current when it executes, so that the query cannot silently read outside the transaction I opened.
17. As an application developer, I want enumerating a query whose connection has been disposed to fail with a clear error, so that a lifetime mistake is obvious rather than mysterious.
18. As an application developer, I want an expression that cannot be translated to SQL to throw a dedicated translation exception, so that I can map it to a client error rather than a server error.
19. As an application developer, I want a query that reaches the database and fails there to throw the existing query exception carrying the SQL, so that failures diagnose the same way as failures from the Dapper adapter.
20. As an application developer, I want no linq2db type to appear in any signature I have to name, so that my code does not acquire a compile-time dependency on the provider this library happens to use.
21. As an application developer, I want no linq2db exception type to escape into my catch blocks, so that my error handling depends on this library's contract rather than its implementation.
22. As an application developer, I want a `DateOnly`, `TimeOnly`, `Uri` or JSON-dictionary column on my **Table definition** to materialize correctly through `Query()`, so that the query surface reads the same types the Dapper surface does.
23. As an application developer with a custom type mapping, I want a hook to register a conversion with the query surface, so that my own types work through `Query()` the way my Dapper type handlers make them work through `Dapper`.
24. As an application developer, I want a build warning when a **Table definition** has a property type the query surface cannot map, so that I learn about it at build time rather than when the query runs.
25. As an application developer, I want the existing generated create, read, update and delete methods to behave exactly as before, so that adopting `Query()` does not put my working code at risk.
26. As an application developer, I want the column names, schema qualification and primary-key designation used by `Query()` to come from the same **Table definition** as the existing generated SQL, so that the two surfaces can never disagree about what the table looks like.
27. As an application developer, I want to set a command timeout on a query, so that I can bound a query I know may be expensive.
28. As an application developer, I want the timeout to be optional with no library-imposed default, so that the query surface behaves like every other adapter in this library.
29. As a library maintainer, I want the linq2db mapping registration emitted by the source generator rather than discovered by reflection, so that the library stays trimming- and AOT-friendly.
30. As a library maintainer, I want a single shared mapping schema instance, so that linq2db's per-schema query translation cache is actually reused instead of being discarded on every request.
31. As a library maintainer, I want the linq2db context to never own or close the underlying connection, so that `DatabaseConnection` remains the single owner of connection lifetime.
32. As a library maintainer, I want the async path to be verified as genuinely asynchronous, so that the exception-wrapping layer cannot silently degrade it to synchronous enumeration.
33. As a library maintainer, I want the decision to take a linq2db dependency recorded as an architecture decision, so that a future reader understands why a thin Dapper wrapper depends on an ORM.

## Implementation Decisions

### Provider selection

- **linq2db** is the LINQ provider. Version 6.3.0 or later — MIT licensed, targeting `net8.0`, `net9.0` and `net10.0`, which matches this library's `TargetFrameworks` exactly.
- The `linq2db` package only. `linq2db.PostgreSQL` is a T4 scaffolding-template package and is explicitly not wanted.
- The dependency goes in the **core library package**. A satellite package cannot add a property to `DatabaseConnection`, and the source generator already ships inside the core package's analyzer. Every consumer of `mvdmio.Database.PgSQL` will therefore acquire linq2db transitively; this is an accepted cost.
- Null-comparison semantics use linq2db's default `CompareNulls.LikeClr`, which matches C# semantics — a not-equals comparison against a nullable column returns rows where the column is null. No configuration required; do not override it.
- The PostgreSQL dialect version defaults to the newest linq2db offers, with an override available on the adapter.
- The analyzer project takes no new dependency. It emits source that references linq2db; it does not reference linq2db itself.

### The `Linq` adapter

- `DatabaseConnection` gains a `Linq` property, mirroring the existing `Dapper`, `Management` and `Bulk` adapters.
- The adapter holds a single, lazily-created linq2db context. It is constructed over the `DatabaseConnection`'s existing connection as a **non-owning** context, so disposing it never closes or disposes the connection.
- When an ambient transaction is present, the context is constructed bound to that transaction. The ambient transaction is tracked by reference identity; when it changes — a transaction begins, commits, or rolls back — the existing context is disposed and rebuilt against the new state. Queryables outstanding from a superseded transaction become invalid, which is the correct outcome.
- The adapter's context is disposed together with the `DatabaseConnection`.
- The adapter exposes a mapping-schema customization hook so consumers can register conversions for their own types. Beyond that hook and the plumbing that `Query()` uses, the adapter exposes nothing in v1 — in particular it does not expose the linq2db context, table, or any other linq2db type.
- Command timeout is an optional per-call value with no library-imposed default, mirroring the Dapper adapter's existing `TimeSpan?` parameter.

### Generated code

- Each generated repository gains `Query()` returning `IQueryable<{Entity}Data>`, declared on both the repository class and its interface.
- The generator emits a linq2db fluent mapping registration per **Table definition**, derived from the same parsed model that already produces the SQL literals: schema name, table name, per-property column names, and primary-key designation. Both surfaces deriving from one model is what prevents them from drifting.
- All mapping registrations contribute to a **single static mapping schema instance** shared process-wide. linq2db caches query translation per mapping-schema instance; constructing a schema per request or per connection silently discards that cache.
- Existing generated methods — create, get-all, get-by, update, delete-by — remain on Dapper and are not rewritten. Repositories become hybrid by design.
- The dependency-injection registration extension is unchanged.

### Type mapping

- The generator emits linq2db value converters for the four types the library ships Dapper handlers for: `DateOnly`, `TimeOnly`, `Dictionary<string,string>` mapped to `jsonb`, and `Uri`. Dapper's type-handler registry and linq2db's mapping schema are entirely separate; without these converters, a **Table definition** carrying one of those types fails to *materialize* through `Query()`, not merely to filter on it.
- linq2db's historical handling of `DateOnly` and PostgreSQL `TimeOnly` has been unreliable in earlier versions. Verify both against the version actually referenced and let the generated converters cover whatever the provider does not.
- The two consumer-extensible Dapper bases — the enum-as-string handler and the generic JSONB handler — have no automatic linq2db counterpart. Consumers register equivalents through the adapter's mapping hook.
- The generator emits a **warning** — not an error — when a **Table definition** property's type falls outside a known-mappable allowlist: the primitive types, `string`, `Guid`, `decimal`, `byte[]`, the date and time family, enums, and the four converted types above. The diagnostic points at the mapping hook.
- No per-column capability metadata is generated. Restricting which columns may be filtered or sorted is not expressible through `IQueryable<T>` and is left entirely to the consumer.

### Errors

- `Query()` returns a **decorating** `IQueryable<T>` and `IQueryProvider` wrapping linq2db's. The decorator exists so exceptions are translated on every execution path — including a framework enumerating the queryable directly, which never touches this library's extension methods.
- The decorator's provider must rewrite the expression tree before handing it to linq2db, replacing the constant node holding the decorator with the inner queryable. Without this, linq2db encounters a foreign type at the root of every query.
- The decorator must implement linq2db's async query-provider interface. An implementation lacking it falls back to synchronous enumeration, turning every awaited query into a blocked thread — a silent failure, and the single most important thing to test about this layer.
- Two exception types, both deriving from the existing `DatabaseException`:
  - A new **query translation exception** for expressions that cannot be translated. No SQL exists at this point, so the existing query exception — which requires non-null SQL text — does not fit. This is the type a consumer maps to a client error.
  - The **existing query exception** for failures that occur once SQL has reached the database, carrying the rendered SQL, consistent with the Dapper adapter.
- `QueryException.Sql` remains non-null; its contract is unchanged.

### Async

- Thin extension methods in this library's namespace forward to linq2db's async extensions: at minimum list materialization, first-or-default, single-or-default, count, long-count and any, each accepting a `CancellationToken`. They mirror the naming already used on the Dapper adapter.
- The queryable is consumable as `IAsyncEnumerable<T>`.
- These forwarders are also an exception-translation boundary, consistent with the decorator.

### Versioning and documentation

- Bump `PgSqlVersion` in `Directory.Build.props` to **0.31.0**.
- Update the root and library READMEs to document the query surface. READMEs are user-facing usage documentation only — how to call `Query()` and what it supports. No decision-record links, no changelog, no test notes.
- The provider choice is recorded in [ADR 0004](../../docs/adr/0004-linq2db-as-the-queryable-provider.md).

## Testing Decisions

A good test here asserts observable behavior: what rows come back, what the database contains afterwards, what exception type surfaces, and whether an await actually yields. It does not assert on generated source text beyond what is needed to observe a diagnostic, and it does not reach into the adapter's internals to check which context instance is in play. Where a test needs to know that work happened *in the database* rather than in memory, it asserts on the observable consequence — for example, that a filtered query against a large seeded table returns quickly and that ordering matches PostgreSQL's collation rather than .NET's.

### Primary seam: `GeneratedRepositoryTests` (integration)

This is the highest available seam and should carry the bulk of the coverage. It already exercises the `UserTable` **Table definition** end to end against a real PostgreSQL container through `TestBase`, using the real generated `UserRepository`. Extending it covers the generator, the emitted mapping, the adapter, the decorator, linq2db, PostgreSQL and materialization in a single pass.

`TestBase` opens a transaction in `InitializeAsync` and rolls it back in `DisposeAsync`. That is not incidental — it means every test in this class implicitly exercises ambient-transaction binding. A test that writes rows through the Dapper path and reads them back through `Query()` proves the adapter is bound to the ambient transaction; if it were not, the test would see nothing.

Coverage to add here:

- Filtering, ordering, paging, the aggregate operators, and the single-row operators — first, first-or-default, single, single-or-default — each asserted against known seeded rows.
- A filter built from a runtime local variable becomes a SQL parameter rather than an inlined literal, so PostgreSQL can reuse the plan. Assert on the rendered SQL, which is the only place this is observable.
- Read-your-own-writes: rows created via the repository's Dapper-backed create are visible to `Query()` within the same transaction.
- A transaction that begins after `Query()` is composed: the query executes against the current transaction, not a stale context.
- Null semantics: a not-equals filter against a nullable column returns the rows where that column is null.
- Type round-tripping for `DateOnly`, `TimeOnly`, `Uri` and the JSON-dictionary type — these need a **Table definition** fixture carrying those columns, which the existing `UserTable` does not. Add one rather than widening `UserTable`, so the existing CRUD tests stay unchanged.
- An untranslatable expression throws the query translation exception, and no linq2db exception type escapes.
- A database-level failure throws the existing query exception with SQL attached.
- Enumerating after the connection is disposed throws a clear error. Prior art: `DatabaseConnectionDisposeTests`.
- The async path is genuinely asynchronous, not sync-over-async. This guards the decorator's async-provider implementation, which is the layer most likely to regress invisibly.

### Secondary seam: `TableRepositoryGeneratorTests` (analyzer)

Unavoidable as a second seam, because a build-time diagnostic can only be observed at compile time. Existing style compiles a source snippet against inline runtime stubs and asserts on the result; the stubs will need extending to cover the `Linq` adapter surface the generated code references.

Coverage: the unmappable-property-type warning fires for a property outside the allowlist, and does not fire for each allowlisted type. Assert on the diagnostic, not on the shape of the emitted mapping — the emitted mapping is proven by the integration seam.

### Unit tests

Any expression-rewriting logic in the decorator that can be separated from a live connection belongs in the unit project, following the pattern of the existing parser and selector tests. Prefer pushing logic to where it can be tested without Docker, but do not invent a seam purely to make something unit-testable that the integration seam already covers.

## Out of Scope

- **`$expand`, navigation properties, and joins across Table definitions.** The source generator has no relation model at all — there is no foreign-key or relationship attribute — so this is blocked on a design that does not exist yet. linq2db's OData expand path additionally requires enabling multiple queries and issues N+1 rather than a join. Tracked separately in [the table-relations idea](../table-relations/IDEA.md).
- **Mutation through the query surface.** No bulk update or delete composed from a query. The query surface is read-only; mutation stays on the existing generated methods and the Dapper and Bulk adapters.
- **Guardrails.** No page cap, no row-count ceiling, no statement-timeout default, and no per-column filterable/sortable metadata. Constraining an exposed query surface — including tenant scoping, which cannot be expressed safely at this layer — is the consumer's responsibility, consistent with how every other adapter in this library behaves.
- **Any OData dependency, including a test-only one.** The library is tested through the interface it offers, not through a guess at how one consumer will use it.
- **Rewriting the existing generated CRUD methods onto linq2db.**
- **Exposing linq2db's context, table type, or any other linq2db type** through the adapter or the generated repositories.

## Further Notes

### Risks carried knowingly

1. **linq2db's compatibility with OData's `$select` projections is unproven.** OData rewrites a selected query into a projection onto internal wrapper types, and there are open linq2db issues covering OData query-generation failures — notably `$apply` aggregation. Because no OData test dependency is in scope, the first real proof arrives in the consuming application. This is an accepted consequence of keeping the library front-end-agnostic, not an oversight.
2. **Transaction rebinding is the prototype-first item.** linq2db's documented API binds a transaction at context construction; whether a context can rebind afterwards was not confirmed. The dispose-and-rebuild approach is the design precisely because it does not depend on rebinding being possible — but prove it before building the rest on top.
3. **`DateOnly` and `TimeOnly` on PostgreSQL** have a history of provider bugs. Verify against the referenced version early; the generated converters are the fallback.
4. **Silent async degradation** through the decorator is the highest-consequence, lowest-visibility failure mode in this design. It has an explicit test for that reason.

### Sequencing suggestion

The two riskiest decisions — ambient-transaction rebinding and the decorating provider's async support — are both foundational and both unproven. Prove them against a live connection before generating anything, so that a wrong assumption invalidates a spike rather than the generator, the mapping emission, and the test suite built on top.
