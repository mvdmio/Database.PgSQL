# OData query conformance for the Query surface

Status: ready-for-agent

## Problem Statement

The **Query surface** exists because a consuming application needed `IQueryable<T>` for a **Query front-end** — an OData endpoint specifically. That surface shipped, but nobody has ever run an OData query through it. The **translation boundary** is therefore entirely unknown: a consumer wiring an OData endpoint onto a generated repository's `Query()` today is the first person to find out which query options work.

That uncertainty is worse than it sounds, for three reasons.

First, the boundary cannot be derived. `IQueryable<T>` is an unbounded contract, and the provider underneath is a third-party translator whose OData behaviour is undocumented — there is no integration package, no docs page, no maintained sample, and upstream carries exactly one PostgreSQL regression test which asserts nothing. Reading code cannot answer "does `$select` work"; only running it can.

Second, the default configuration is silently wrong. ASP.NET Core OData decides how defensively to rewrite a filter by matching the query provider's namespace against a hardcoded allowlist of Microsoft providers. The provider behind the Query surface is not on that list, so OData assumes the worst and wraps every property access in a null guard. Left on, `substring` fails to translate at all, collection `all()` returns the wrong rows, and every filter reaches PostgreSQL wrapped in non-sargable `CASE WHEN col IS NULL` expressions. The fix is one setting on the consumer's side, and it is not discoverable — the upstream request to make it automatic has been open since 2022.

Third, several property types the source generator happily accepts have unknown behaviour in an OData model. The generator's mappable-type allowlist admits `Uri`, `Dictionary<string, string>`, `DateOnly`, `TimeOnly`, `TimeSpan` and `byte[]`; whether OData's convention model builder maps, ignores, or rejects each of them is unverified. A consumer generating a repository from a real table can hit this at startup with no warning from us.

## Solution

A conformance suite: a separate test project that runs real OData query options against the Query surface over a real PostgreSQL database, and asserts both the rows returned and the SQL produced.

It answers, per query option and per `$filter` function, whether the Query surface handles it — and pins each answer as a test, so the boundary becomes a regression-guarded artifact instead of folklore. Where a query option requires specific OData configuration to work, the suite is configured that way and the requirement is asserted, not just documented.

Because the thing under test is expression translation, the suite drives OData in-process: it constructs query options from a query string and applies them directly to the queryable. No web host, no HTTP, no controllers. This keeps the suite fast and focused, and it keeps the front-end dependency out of everything the project ships.

The project doubles as the reference example. Its README is the wiring walkthrough a consumer needs — the settings that must be set, the functions that must be blocked, the semantics that differ from an Entity Framework Core-backed endpoint, and the failure mode that only appears once the query surface is behind a real HTTP endpoint.

## User Stories

1. As a package consumer, I want to know which OData query options the Query surface answers correctly, so that I can decide whether it fits my API before I build on it.
2. As a package consumer, I want to know which OData `$filter` functions translate to SQL, so that I can document my API's supported query surface to my own clients.
3. As a package consumer, I want a worked example of wiring OData onto a generated repository's `Query()`, so that I do not have to derive the configuration from first principles.
4. As a package consumer, I want the mandatory null-propagation setting stated prominently, so that I do not ship an endpoint that silently returns wrong results.
5. As a package consumer, I want to know why that setting is mandatory, so that a future maintainer of my code does not "clean it up".
6. As a package consumer, I want the unsupported functions blocked by configuration, so that my clients get a validation error rather than a server error.
7. As a package consumer, I want to know that `ne` on a nullable column returns different rows than an Entity Framework Core-backed endpoint would, so that I can assess the risk before replacing one behind an existing API.
8. As a package consumer, I want to know which generated property types are unusable in an OData model, so that I can plan around them rather than discover them at startup.
9. As a package consumer, I want to know what happens when an untranslatable query reaches a hosted endpoint, so that I can add error handling before my users see a truncated response.
10. As a package consumer, I want `$select` to narrow the columns actually queried, so that projection is a performance win rather than cosmetic.
11. As a package consumer, I want `$top` and `$skip` to reach the database as `LIMIT`/`OFFSET`, so that paging does not materialize the whole table.
12. As a package consumer, I want `$count` to reach the database as an aggregate, so that counting does not materialize rows.
13. As a package consumer, I want `$orderby` to sort in the database, so that ordering is not done in memory.
14. As a package consumer, I want `$filter` to reach the database as a `WHERE` clause with parameters, so that predicates are neither client-evaluated nor inlined into SQL text.
15. As a package consumer, I want `$apply` grouping and aggregation to translate to `GROUP BY`, so that I can expose aggregate endpoints without hand-written SQL.
16. As a package consumer, I want the suite to exercise the generated repository's `Query()` rather than the adapter directly, so that what is proven is the path I actually call.
17. As a library maintainer, I want the boundary captured as tests, so that a provider upgrade that breaks a query option fails the build instead of reaching consumers.
18. As a library maintainer, I want the front-end dependency confined to a non-packable project, so that the front-end-agnostic guarantee in ADR 0004 stays true of everything shipped.
19. As a library maintainer, I want the suite to assert generated SQL and not just returned rows, so that a regression from an index-using predicate to a non-sargable one is caught.
20. As a library maintainer, I want a regression test pinning the misconfigured behaviour, so that nobody simplifies the mandatory setting away without a failing test.
21. As a library maintainer, I want the awkward generated property types documented by test, so that the generator's mappable-type allowlist and the front-end's model builder are known to agree or known to differ.
22. As a library maintainer, I want no new public API for this work, so that a test-only need does not become a support commitment.
23. As a library maintainer, I want the suite isolated in its own assembly, so that the front-end dependency does not leak into the main integration suite.
24. As a library maintainer, I want each test to leave no database state behind, so that the suite can run repeatedly and in any order.
25. As a library maintainer, I want the deferred follow-ups recorded as triageable ideas, so that findings from this work are not lost when the session ends.
26. As a contributor, I want the suite to follow the existing integration test patterns, so that I can read and extend it without learning a second convention.
27. As a contributor, I want to know which query options are deliberately out of scope, so that I do not read a gap as a bug.
28. As a contributor, I want a failing conformance test to tell me whether the fault is ours or the front-end's, so that I know where to fix it.
29. As a contributor, I want the suite runnable on its own, so that I can iterate on it without running the whole solution.
30. As a future implementer of table relations, I want to know exactly what `$expand` requires from a relation model, so that the design accounts for it up front.

## Implementation Decisions

### Shape and placement

A new test project, `mvdmio.Database.PgSQL.Tests.Integration.OData`, under `test/`, added to the solution's `test` folder. Non-packable. Single-target `net10.0`, matching the existing integration project. `SecondarySchema` is the precedent for an independent test-support project that carries its own fixtures rather than reaching into a sibling.

Deliberately **not** a web project. `[EnableQuery]` is an MVC action filter and the minimal-API equivalent is an endpoint filter; both require a host, and neither is what is under test. Instead the suite constructs query options over a stand-alone request and applies them to the queryable — a pattern the front-end library supports explicitly (its query-context constructor is documented as being for stand-alone use with no service container) and uses throughout its own test suite.

Because there is no host, the project uses the plain SDK rather than the web SDK. It therefore needs an explicit framework reference to `Microsoft.AspNetCore.App`: the front-end package's own dependencies are only its OData libraries, and the ASP.NET Core types it needs come from the shared framework. Without that reference the project restores but does not compile.

### Front-end version

The current stable front-end release, whose only assembly targets `net8.0` but loads and runs correctly on the `net10.0` runtime — verified empirically, not assumed. The 10.x preview line is `net10.0`-native but breaking (it changes the CLR types behind `Edm.Date` and `Edm.TimeOfDay`) and has no release notes; not worth taking for a test project.

### Fixture

Own assembly fixture, own PostgreSQL container of the same pinned image as the existing suite. A second container in the test run is the accepted cost of assembly isolation.

Own copy of the connection-and-transaction base class: connection built by the factory, transaction opened before each test and rolled back after. Duplicated rather than shared by project reference — the same choice `SecondarySchema` makes — so the two test assemblies stay independent. With no HTTP boundary, the rollback pattern works here exactly as it does in the existing suite.

Table DDL committed directly by the fixture rather than expressed as a migration, mirroring how the existing suite sets up its query-surface table. This project ships no migrations and asserts nothing about migrations.

### Fixture entities

**Two** table definitions, and the split is load-bearing.

The first is the conformance entity: an EDM-friendly column set chosen to cover the surface under test — text and nullable text for the string functions, signed-integral and decimal columns for the arithmetic functions and for `$apply` aggregation, an offset-bearing timestamp column for the date functions, a boolean, an enum, a unique identifier, and a low-cardinality text column to group by. Nullable columns are included specifically so null-comparison semantics are covered rather than assumed. Every type here has a direct EDM primitive equivalent, so this entity's model must build cleanly — if it does not, that is a bug in the suite, not a finding.

The second is the awkward-types entity, carrying the property types the generator's mappable-type allowlist admits but whose model-builder behaviour is unknown. There are more of these than the obvious ones: alongside `Uri`, `Dictionary<string, string>`, the date-and-time-only types, durations and byte arrays, the allowlist also admits `char`, the signed byte, and all three unsigned integer widths — and the EDM primitive set has no unsigned integer and no character type at all. Plain `DateTime` belongs here too rather than on the conformance entity, since EDM offers only an offset-bearing instant, a date and a time-of-day, so the mapping is a convention rather than an equivalence.

The split is load-bearing because the failure mode is unknown: if the convention model builder throws on one of these types, having it on the conformance entity would break every test in the suite rather than the one test that is asking the question.

Both are declared as **Table definitions** so the generated repositories, their `Query()` methods, and the module-initializer mapping registration are all exercised. The suite calls the generated repository, not the `Linq` adapter — that is the higher seam and the one consumers use.

### Configuration

A single shared helper in the test project owns the recommended configuration, so there is one place to point the README at and one place to change:

- **Query settings** with null-propagation handling disabled. This is not a tuning choice. Left at its default the front-end guards every property access, which breaks `substring` outright, makes collection `all()` return wrong rows, and renders every predicate non-sargable. The default is selected by namespace-matching the query provider against a hardcoded list of Microsoft providers, which this library's provider is not on and cannot join from our side.
- **Validation settings** whose allowed-functions set excludes the functions known not to translate: pattern matching (the provider's maintainers have declined to implement it), type checks against primitive properties, and the min/max-datetime functions (which the front-end itself does not implement). Excluding them turns a server-side translation failure into a client-side validation error.

Note an asymmetry the README must call out: in-process, validation is a separate explicit step, whereas a hosted endpoint's attribute performs it automatically. A consumer copying only the query settings and not the validation settings gets a working endpoint with a worse error contract.

### Library changes — internal only, no public surface

Two changes, both internal:

1. The test assembly is added to the library's internals-visible list, alongside the two existing test assemblies. Note before editing: that list is currently declared **twice** in the project file, in two separate item groups using the short and the full attribute name, each naming the same two test assemblies. It builds — the attribute permits multiples — but the new entry should go in one place, and consolidating the pre-existing duplication is a reasonable incidental tidy.
2. The internal diagnostics helper gains a **non-generic** SQL-rendering overload, and the SQL-rendering and last-SQL members are lifted onto the internal decorator interface so that overload can reach them without knowing the element type.

The second change is not a convenience. After `$select`, the applied queryable's element type is one of the front-end's own wrapper types, which are `internal` to its assembly and therefore cannot be named as a generic argument. The existing generic-only signature is unusable for precisely the case where inspecting SQL matters most — proving that `$select` narrows the column list. The generic overloads become thin delegations to the non-generic one.

No public API changes. Version bumped `0.31.0` → `0.31.1` (PATCH): the library changes, but nothing a consumer can reference does.

### Documentation

- A README in the new project: the wiring walkthrough, the mandatory settings and why, the blocked functions, the conformance results as a table, the null-comparison divergence from an Entity Framework Core-backed endpoint, and the hosted failure mode described below.
- Pointers to it from the library README's existing OData section and from the root README.
- ADR 0004 amended (already done): the front-end-agnostic claim narrowed from "the library or its tests" to "the shipped packages", plus consequences recording that conformance is proven out-of-band and that consumers must disable the front-end's null-propagation rewriting.
- The domain glossary gains **Query front-end** and **translation boundary** (already done); both are used throughout this spec.

### The hosted failure mode — documented, not tested

A hosted endpoint's query attribute composes the expression tree but never enumerates it; materialization happens later, inside the output formatter, after response headers have already been written. A provider exception at that point therefore produces a success status with a partial body and an aborted stream, not an error status — the output formatter has no exception handling at all.

The in-process suite cannot observe this, because it materializes the queryable itself and sees a clean exception. It is nonetheless the single most consequential thing a consumer needs to know, since the Query surface's careful translation exceptions are worthless if the front end turns them into a truncated response. It goes in the README as a warning with the recommended mitigation (materialize inside the action and map failures to a status code), and is recorded as a known gap in ADR 0004.

### Deferred follow-ups, filed as ideas

Four ideas filed under the issue-tracker convention as `query-column-nullability`, `odata-integration-package`, `odata-provider-allowlist-upstream` and `ci-test-job`, each `Status: needs-triage`:

- **Column nullability in generated mappings.** The generator emits no nullability information, so the provider assumes every column is nullable and its CLR-like null-comparison mode adds `OR col IS NULL` to every inequality — including on columns that cannot be null. Non-sargable, and fixable, but it changes a public builder signature and the generator, so it is out of scope here.
- **An opt-in OData integration package** shipping a preconfigured query attribute, so consumers stop falling into the null-propagation trap. Deferred because a third shipped package is a real versioning and support commitment, and because the front-end's major versions break.
- **An upstream contribution** adding this library's provider namespace to the front-end's allowlist, which would make the correct behaviour the default for everyone. Correct fix, multi-year lead time on current evidence.
- **A CI test job.** The publish pipeline runs no tests today and triggers only on library changes, so this project changes nothing about CI. Worth fixing, separately.

## Testing Decisions

The suite *is* the deliverable, so "what makes a good test" is the spec's core question rather than an afterthought.

A good test here asserts external behaviour: the rows a query returns, and the SQL it sends. Both are external — the SQL is what the database receives, and it is the only place several of the properties under test are observable at all. Column narrowing under `$select`, `LIMIT`/`OFFSET` for paging, an aggregate for `$count`, and parameterization rather than literal inlining cannot be distinguished from a correct row set. Asserting SQL shape is the point, not an implementation-detail leak — but assertions should target the shape that matters (a `GROUP BY` is present, a column list is narrowed, a predicate is parameterized) rather than pinning entire SQL strings, which would break on every harmless provider change.

Each conformance test names the query option or function under test and states the outcome, following the existing naming convention.

Coverage:

- **Per query option**: `$filter`, `$orderby`, `$top`, `$skip`, `$count`, `$select`, `$apply`. For each, rows and SQL shape.
- **Per `$filter` function**, one test each, across the string, date-part, arithmetic, cast and membership families — grouped so a family's results read as a table.
- **Null-comparison semantics**: inequality against a nullable column includes the null rows. This is the OData-specified behaviour and it diverges from an Entity Framework Core-backed endpoint. Asserting it pins a real, externally-visible contract.
- **The misconfiguration regression test**: with the mandatory setting left at its default, `substring` fails to translate and predicates render with null-guard wrappers. This is a deliberately narrow test — the setting's worst symptom is silent empty results on `$expand`, which is out of scope, so what remains provable is this. It is a weaker guard than the risk warrants, which is itself worth a comment in the test pointing at the upstream issue.
- **Blocked functions**: each excluded function produces a validation error rather than reaching the provider.
- **The awkward types**: one test per type recording what the convention model builder does with it. Whatever the answer, the test documents it; these are characterization tests and should say so.
- **Untranslatable expressions**: a query the provider cannot translate surfaces as the library's query translation exception, not as a provider exception and not as a silent in-memory fallback.

Prior art to follow closely: the existing generated-repository query tests are the direct model — same base class, same transaction-per-test isolation, same use of the internal diagnostics helper for the assertions only observable in SQL. The existing fixture is the model for container and DDL setup. `SecondarySchema` is the model for an independent test-support project.

No mocking framework, consistent with the rest of the repo. There is nothing to mock: the front-end, the provider and the database are all real, which is the entire value of the suite.

## Out of Scope

- **`$expand`.** It needs a relation model, which does not exist. The research is worth carrying into that work: expansion maps onto the provider's eager-loading path and does function, but requires an association declared on the member with an explicit foreign-key property — and with the null-propagation default left on it returns empty collections silently, having issued the child queries and discarded the rows. Revisit when table relations land.
- **`$search`, `$compute`, `$skiptoken`, `$batch`.**
- **Hosting anything.** No controllers, no minimal-API endpoints, no web host, no `WebApplicationFactory`. The hosted pipeline's failure behaviour is documented, not tested.
- **Multi-targeting.** The library's framework coverage is proven by its own build; re-proving it through a front-end buys little for a lot of project-file complexity.
- **Public API for SQL inspection.** The diagnostics helper stays internal.
- **The four filed ideas**: generator nullability, an opt-in integration package, the upstream allowlist contribution, and a CI test job.
- **Fixing the latent cross-root rewrite issue** in the expression-tree rewriter, where every decorator constant is replaced with the current query's root regardless of which query source it belongs to. Unreachable from anything in scope here, since it needs a second queryable in one expression. Should be revisited with `$expand`.

## Further Notes

**The evidence base.** The scope above rests on empirical work, not documentation, because documentation does not exist. A live harness against the current provider and front-end over a real PostgreSQL database established that `$select`, `$filter` (most functions), `$orderby`, `$top`/`$skip`, `$count` and `$apply` all work and produce reasonable SQL. Two findings inverted expectations and are worth stating because they will shape how the suite reads: `$select` works and narrows columns properly, despite the front-end projecting into its own internal wrapper types and despite historical bug reports to the contrary; and `$apply` grouping and aggregation is the *best*-supported area, including grouping over a navigation property — a case the provider's own upstream test file still has commented out.

**An existing decision, validated.** ADR 0004 kept the provider's CLR-like null-comparison mode because it matches C# semantics. It is also the mode the OData specification requires — Part 2 §5.1.1.1 states that the null value is not equal to any value but itself — which makes this library's behaviour *more* specification-correct than an Entity Framework Core-backed endpoint, which drops the null rows. Switching to SQL-like comparison for cleaner generated SQL would silently change API semantics and drop rows. The README should say so, because the generated SQL is visibly noisier and the temptation is real.

**Ownership.** There is no integration package, no documentation page, no maintained sample, and one un-asserting upstream regression test for this provider-and-front-end combination. Median upstream fix latency for anything non-trivial is measured in months, sometimes years. This suite is the only conformance evidence that will exist for it.
