---
status: accepted
---

# Adopt linq2db as the LINQ provider behind generated repositories

Generated repositories bake every query into a SQL string literal at generation time, so no query whose shape is decided at runtime is expressible through them. A consuming application needed `IQueryable<T>` specifically — not merely the capability of composing filters — because several candidate query front-ends it is evaluating, an OData endpoint among them, all take `IQueryable<T>` as their interchange type. We decided to satisfy that by taking a dependency on **linq2db** in the core library package and exposing a `Linq` adapter on `DatabaseConnection`, alongside the existing `Dapper` and `Bulk` adapters, with generated repositories gaining `Query()`.

`IQueryable<T>` is an unbounded contract: every LINQ operator is compile-legal against it, so the set of expressions a provider must handle is not something the type system can cap. That property is what makes the choice of *who owns the translator* the decision worth recording.

## Considered options

- **linq2db behind a `Linq` adapter (chosen).** MIT licensed, actively maintained, targeting `net8.0`/`net9.0`/`net10.0` — an exact match for this library's frameworks. Provides the PostgreSQL dialect, async materialization, `IAsyncEnumerable` support, and fluent mapping that keeps provider attributes off the generated data types. Its default null-comparison mode already matches C# semantics, which is the behavior we want and would otherwise have had to build. A context constructed over an externally-owned connection or transaction, non-owning, lets `DatabaseConnection` remain the sole owner of connection lifetime.
- **Hand-roll an `IQueryProvider`.** Rejected. Viable in principle — the translated surface can be bounded by translating only up to the first projection and evaluating the rest client-side — but it means owning an expression translator, a partial-evaluation pass, PostgreSQL dialect decisions, three-valued-logic handling, and an async story, against an input grammar that is open by construction. The cost is not one-time: it recurs every time a consumer writes an operator nobody anticipated.
- **Translate the OData AST directly to SQL, with no `IQueryProvider` at all.** Rejected, though it was the strongest option on pure engineering merit. The OData abstract syntax tree is a closed, specified grammar where the LINQ expression tree is an open one; an unsupported construct could be rejected as a client error rather than surfacing as a server error; and `$select` reduces to a column list. It was rejected for two reasons that outrank that: it binds a general-purpose PostgreSQL library to one specific query front-end, and the consuming application needs `IQueryable<T>` for front-ends other than OData.
- **Entity Framework Core.** Rejected. A far heavier dependency, bringing change tracking, an identity map, and a model-building story this library has no use for, in exchange for provider capability we do not need at single-table scope.

## Consequences

- **Every consumer of the package acquires linq2db**, whether or not they declare a **Table definition**. A satellite package cannot add a property to `DatabaseConnection`, and the source generator ships inside the core package's analyzer, so opt-in packaging was not available without a materially worse API. This is the main cost of the decision.
- **The library remains front-end agnostic.** It produces a standard `IQueryable<T>` and depends on no query front-end. OData drove the requirement but appears nowhere in the library or its tests.
- **linq2db types never appear in a signature a consumer must name** — not the context, not the table type. `Query()` returns `IQueryable<T>`. Keeping the provider replaceable in principle was a design goal; in practice replacing it would still be a major change because query *behavior* is part of the contract.
- **A decorating queryable and provider are required**, not optional, to keep linq2db exceptions from escaping into consumer catch blocks. That decorator must rewrite the expression tree root and implement linq2db's async provider interface — omitting the latter silently degrades every awaited query to synchronous enumeration.
- **Repositories become hybrid**: existing create, read, update and delete methods stay on Dapper with their explicit `RETURNING` clauses; only `Query()` runs through linq2db. Both surfaces are generated from the same parsed **Table definition**, so they cannot drift in their view of the table.
- **Dapper type handlers do not carry over.** Dapper's handler registry and linq2db's mapping schema are separate, so the library's four built-in handled types need generated linq2db converters, and consumer-registered handlers need an equivalent registered through the adapter's mapping hook. Without this a column does not merely fail to filter — it fails to materialize.
- **Cross-table querying is not unlocked by this decision.** linq2db supports joins, but the source generator has no relation model to generate them from, so the query surface is single-table until that is designed.
- **API surface.** Additive: `DatabaseConnection` gains `Linq`, generated repositories and their interfaces gain `Query()`, and a query translation exception joins the existing exception hierarchy. MINOR bump.
