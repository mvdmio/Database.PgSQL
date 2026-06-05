# mvdmio.Database.PgSQL

C# NuGet package wrapping Dapper for PostgreSQL. Simplifies connections + query execution, transactions, migrations, bulk ops (copy/upsert), and management tasks (schema/table existence). Targets .NET 8.0, 9.0, 10.0. Work style: telegraph, low-filler, direct.

Package manager: NuGet.

## Universal Rules

- Ask if you need clarification or the design is unclear.
- Search early. Quote exact errors. Prefer newer sources.
- Check worktree first. Do not revert user changes.
- Keep files under ~500 LOC; refactor as needed (not test files). Do NOT split classes into partial files — refactor properly instead.
- Always add tests for new code. Always create/modify tests when changing existing code.
- Always build the solution and run the tests after changes. Fix all build errors and test failures before finishing.
- If a build fails due to a file lock: identify and stop the locking process, then retry.
- Always update `README.md` to reflect the latest state of the project.
- Always bump the version in `mvdmio.Database.PgSQL` and `mvdmio.Database.PgSQL.Tool` `.csproj` when changes affect those projects. Semantic versioning (MAJOR.MINOR.PATCH): MAJOR = incompatible API changes; MINOR = backward-compatible features; PATCH = backward-compatible bug fixes.

## Project Structure

```
├── src/
│   ├── mvdmio.Database.PgSQL/              # Main library
│   │   ├── Connectors/                     # Database connector abstractions
│   │   │   ├── Bulk/                       # Bulk operations (copy, upsert)
│   │   │   ├── DapperDatabaseConnector.cs  # Dapper wrapper
│   │   │   └── ManagementDatabaseConnector.cs
│   │   ├── Dapper/                         # Dapper configuration & type handlers
│   │   ├── Exceptions/                     # Custom exceptions
│   │   ├── Extensions/                     # Extension methods
│   │   ├── Migrations/                     # Migration framework
│   │   └── DatabaseConnection.cs           # Main entry point
│   └── mvdmio.Database.PgSQL.Tool/         # CLI tool for migrations
└── test/
    ├── mvdmio.Database.PgSQL.Tests.Integration/
    └── mvdmio.Database.PgSQL.Tests.Unit/
```

## Key Classes

| Class | Purpose |
|-------|---------|
| `DatabaseConnection` | Main entry point. Provides access to Dapper, Management, and Bulk connectors. |
| `DapperDatabaseConnector` | Wraps Dapper methods with proper connection/transaction handling. |
| `ManagementDatabaseConnector` | Database management operations (TableExists, SchemaExists). |
| `BulkConnector` | High-performance bulk operations: Copy, InsertOrUpdate, InsertOrSkip. |
| `IDbMigration` | Interface for implementing database migrations. |
| `DatabaseMigrator` | Migration runner that tracks executed migrations. |

## Build Commands

```bash
dotnet build    # Build all projects
dotnet test     # Run all tests
dotnet format   # Format code per .editorconfig
```

Before committing, run in sequence: `dotnet format` → `dotnet build` → `dotnet test`.

## Testing

Write tests before implementing features (TDD). Tests cover all new code.

- **Unit tests:** `test/mvdmio.Database.PgSQL.Tests.Unit/`
- **Integration tests:** `test/mvdmio.Database.PgSQL.Tests.Integration/` — use Testcontainers to spin up PostgreSQL in Docker. Each test runs in a transaction rolled back after completion.

Conventions:
- Inherit from `TestBase` for integration tests with database access.
- Use `AwesomeAssertions` for fluent assertions.
- Test migrations live in `test/.../Fixture/Migrations/`.
- Descriptive names that explain what is being tested:

```csharp
public async Task QueryAsync_WithValidSql_ReturnsResults()
public async Task BulkCopy_WithEmptyTable_CompletesSuccessfully()
```

## Code Style

Comprehensive `.editorconfig` governs formatting. Key conventions:

- **Indentation:** 3 spaces for C# files
- **Namespaces:** file-scoped (`namespace Foo;`)
- **Line endings:** CRLF; final newline required
- Use `var` when type is apparent; braces for multi-line statements; prefer pattern matching; use `?.` and `??`.

Naming:

| Element | Convention | Example |
|---------|------------|---------|
| Classes, Methods, Properties | PascalCase | `DatabaseConnection` |
| Private fields | _camelCase | `_connection` |
| Parameters, Variables | camelCase | `connectionString` |
| Constants | UPPER_SNAKE_CASE | `DEFAULT_TIMEOUT` |
| Interfaces | IPrefix | `IDbMigration` |
| Generic parameters | TPrefix | `TEntity` |
| Async methods | Async suffix | `ExecuteAsync` |

## Documentation

All public methods must have XML doc comments:

```csharp
/// <summary>
/// Executes a SQL query and returns the results.
/// </summary>
/// <typeparam name="T">The type to map results to.</typeparam>
/// <param name="sql">The SQL query to execute.</param>
/// <param name="parameters">Optional query parameters.</param>
/// <returns>An enumerable of mapped results.</returns>
public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
```

## Migrations

Identifiers use timestamp format `YYYYMMDDHHmm` (e.g., `202602161430`):

```csharp
public class AddUsersTable : IDbMigration
{
   public long Identifier => 202602161430;
   public string Name => "AddUsersTable";

   public async Task UpAsync(DatabaseConnection connection)
   {
      await connection.Dapper.ExecuteAsync("""
         CREATE TABLE users (
            id SERIAL PRIMARY KEY,
            name TEXT NOT NULL
         )
         """);
   }
}
```

## Dependencies

- **Dapper** — micro-ORM for database queries
- **Npgsql** — PostgreSQL ADO.NET provider
- **xunit.v3** — test framework
- **Testcontainers.PostgreSql** — PostgreSQL container for integration tests
- **AwesomeAssertions** — fluent assertions library
