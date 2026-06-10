# Testing

Test stack: **xUnit v3** + **AwesomeAssertions** (fluent assertions) + **Testcontainers.PostgreSql** (integration). No mocking framework — use interface seams and hand-written fakes.

TDD is the expectation: write tests before implementing, and always add/modify tests when changing code.

## Test projects

| Project | TFM | Purpose |
|---------|-----|---------|
| `test/mvdmio.Database.PgSQL.Tests.Unit/` | net9.0 | Pure unit tests — no database |
| `test/mvdmio.Database.PgSQL.Tests.Integration/` | net10.0 | Database tests via Testcontainers |
| `test/mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema/` | — | A second assembly with its own migrations/schema, referenced by the integration suite to exercise multi-assembly / multi-scope and embedded-schema scenarios |
| `test/mvdmio.Database.PgSQL.Analyzers.Tests/` | — | Roslyn analyzer tests |

## Unit tests

- Location: `test/mvdmio.Database.PgSQL.Tests.Unit/`.
- No database, no Docker — fast. Favor extracting pure logic into testable units (e.g. `SchemaFileParser`) and covering it here.
- Assert with `AwesomeAssertions`.

## Integration tests

- Location: `test/mvdmio.Database.PgSQL.Tests.Integration/`. **Docker must be running.**
- Inherit from `TestBase`. It builds a `DatabaseConnection` against the shared `Testcontainers` PostgreSQL container, opens a transaction in `InitializeAsync`, and **rolls it back in `DisposeAsync`** — so each test is isolated and leaves no state behind. Use the `Db` property and the `CancellationToken` it exposes.
- Test migrations live under `Fixture/Migrations/`; embedded test schemas under `Schemas/` (embedded with `LogicalName` = filename).
- The `SecondarySchema` project provides a separate assembly when a test needs migrations/schema from more than one assembly.

## Conventions

- Descriptive test names that state what is verified, e.g. `QueryAsync_WithValidSql_ReturnsResults`, `BulkCopy_WithEmptyTable_CompletesSuccessfully`.
- Test only external behavior — observable outputs and database state — not implementation details.
- Prior art: `Migrations/SchemaFileParserTests.cs` (unit), `Migrations/SchemaFirstMigrationTests.cs` (integration).

## Running tests

- Whole solution: `dotnet test`
- One project (prefer this while iterating — unit tests need no Docker):
  `dotnet test test/mvdmio.Database.PgSQL.Tests.Unit/mvdmio.Database.PgSQL.Tests.Unit.csproj`
- Single test by method-name substring:
  `dotnet test test/mvdmio.Database.PgSQL.Tests.Unit/mvdmio.Database.PgSQL.Tests.Unit.csproj --filter "Name~ParseMigrationVersion"`
- By fully-qualified name (class/namespace substring):
  `dotnet test test/mvdmio.Database.PgSQL.Tests.Integration/mvdmio.Database.PgSQL.Tests.Integration.csproj --filter "FullyQualifiedName~SchemaFirst"`
- Keep `dotnet` steps sequential — never run build and test (or two test runs) in parallel, to avoid file locks. Before committing, run in order: `dotnet format` → `dotnet build` → `dotnet test`.
