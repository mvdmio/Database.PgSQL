# Testing

Test stack: **xUnit v3** + **AwesomeAssertions** (fluent assertions) + **Testcontainers.PostgreSql** (integration). No mocking framework — use interface seams and hand-written fakes.

TDD is the expectation: write tests before implementing, and always add/modify tests when changing code.

## Test projects

| Project | TFM | Purpose |
|---------|-----|---------|
| `test/mvdmio.Database.PgSQL.Tests.Unit/` | net9.0 | Pure unit tests — no database |
| `test/mvdmio.Database.PgSQL.Tests.Integration/` | net10.0 | Database tests via Testcontainers |
| `test/mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema/` | — | A second assembly with its own migrations/schema, referenced by the integration suite to exercise multi-assembly / multi-scope and embedded-schema scenarios |
| `test/mvdmio.Database.PgSQL.Tests.Integration.OData/` | net10.0 | OData conformance suite over the query surface — its own container, its own fixtures, and the only project that takes an OData dependency |
| `test/mvdmio.Database.PgSQL.Analyzers.Tests/` | net9.0 | Roslyn analyzer and source generator tests — drives the generator over a source string and asserts on the diagnostics and emitted source |
| `test/mvdmio.Database.PgSQL.Tests.Packaging/` | net10.0 | Packs the library, installs the package into a project scaffolded at test time, builds it for all three target frameworks and runs it against a container. The slowest suite, and the only one that references neither the library nor the analyzer — the artifact under test is the `.nupkg` |

## Unit tests

- Location: `test/mvdmio.Database.PgSQL.Tests.Unit/`.
- No database, no Docker — fast. Favor extracting pure logic into testable units (e.g. `SchemaFileParser`) and covering it here.
- Assert with `AwesomeAssertions`.

## Integration tests

- Location: `test/mvdmio.Database.PgSQL.Tests.Integration/`. **Docker must be running.**
- Inherit from `TestBase`. It builds a `DatabaseConnection` against the shared `Testcontainers` PostgreSQL container, opens a transaction in `InitializeAsync`, and **rolls it back in `DisposeAsync`** — so each test is isolated and leaves no state behind. Use the `Db` property and the `CancellationToken` it exposes.
- Test migrations live under `Fixture/Migrations/`; embedded test schemas under `Schemas/` (embedded with `LogicalName` = filename).
- The `SecondarySchema` project provides a separate assembly when a test needs migrations/schema from more than one assembly.

## The OData conformance suite

- Location: `test/mvdmio.Database.PgSQL.Tests.Integration.OData/`. Its own container, its own copies of `TestBase` and
  the fixture, and the only project referencing `Microsoft.AspNetCore.OData` — so the front-end-agnostic guarantee in
  ADR 0004 stays true of the shipped packages.
- Drives OData in-process (`Fixture/ODataQuery.cs`) with no web host. `Fixture/ODataConfiguration.cs` owns the
  recommended configuration; the project `README.md` is the consumer-facing walkthrough and the results table.
- Tests assert rows *and* SQL shape, because column narrowing, `LIMIT`/`OFFSET`, an aggregate count and parameterization
  are otherwise indistinguishable from a correct row set. SQL is read through the internal `QueryDiagnostics` helper.
- Three fixtures, because a relation property in the conformance EDM model would change results already pinned against it
  and a second entity pair in one model would do it again: `SampleConformanceTestBase` seeds the single-table conformance
  entity against `ODataConfiguration.Model`, `RelationConformanceTestBase` seeds the author-and-book pair against
  `ODataConfiguration.RelationModel`, and `CompositeKeyConformanceTestBase` seeds the tenant project-and-task pair —
  two-column keys, composite relation — against `ODataConfiguration.CompositeModel`.
- When adding a conformance case, put it with the fixture it needs: a query-option case in `QueryOptionConformanceTests`,
  a `$filter` function case in `FilterFunctionConformanceTests` (theories grouped by family), a `$expand` case in
  `ExpandConformanceTests`, and a navigation-path or collection-quantifier case in
  `RelationNavigationConformanceTests`. Composite keys mirror that split in `CompositeKeyConformanceTests` (query options
  and navigation paths) and `CompositeKeyExpandConformanceTests`. The three misconfiguration-regression classes pair up
  the same way — `MisconfigurationRegressionTests` for what a single table shows,
  `RelationMisconfigurationRegressionTests` and `CompositeKeyMisconfigurationRegressionTests` for what needs a relation.
- Model-shape questions need no database and no fixture: `GeneratedTypeModelTests` for column-backed property types,
  `RelationTypeModelTests` for relation properties.

## Conventions

- Descriptive test names that state what is verified, e.g. `QueryAsync_WithValidSql_ReturnsResults`, `BulkCopy_WithEmptyTable_CompletesSuccessfully`.
- Test only external behavior — observable outputs and database state — not implementation details.
- Prior art: `Migrations/SchemaFileParserTests.cs` (unit), `Migrations/SchemaFirstMigrationTests.cs` (integration).

## Packaging tests

- Location: `test/mvdmio.Database.PgSQL.Tests.Packaging/`. **Docker and network access are both required** — the scaffolded consumer restores its transitive dependencies from nuget.org into a package folder under the run's temporary directory.
- Deliberately references nothing from `src/`: a project reference is what made a package shipping without its source generator invisible, since every other suite sees the analyzer that way and none of them looks at the package.
- The library is packed under a run-unique prerelease version, so no stale copy can satisfy the consumer's reference. Note that packing passes `-p:GeneratePackageOnBuild=false`: with that property on — which is how the library is configured — NuGet's `Pack` target does not depend on `Build`, so `dotnet pack` silently packs whatever is already in `bin/`.
- Add to `Fixture/ConsumerProject.cs`'s single table definition rather than adding another consumer: one build per framework is what keeps this suite affordable.

## Running tests

- Whole solution: `dotnet test`
- One project (prefer this while iterating — unit tests need no Docker):
  `dotnet test test/mvdmio.Database.PgSQL.Tests.Unit/mvdmio.Database.PgSQL.Tests.Unit.csproj`
- Single test by method-name substring:
  `dotnet test test/mvdmio.Database.PgSQL.Tests.Unit/mvdmio.Database.PgSQL.Tests.Unit.csproj --filter "Name~ParseMigrationVersion"`
- By fully-qualified name (class/namespace substring):
  `dotnet test test/mvdmio.Database.PgSQL.Tests.Integration/mvdmio.Database.PgSQL.Tests.Integration.csproj --filter "FullyQualifiedName~SchemaFirst"`
- Keep `dotnet` steps sequential — never run build and test (or two test runs) in parallel, to avoid file locks. Before committing, run in order: `dotnet format` → `dotnet build` → `dotnet test`.
