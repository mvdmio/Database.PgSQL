# Agent Instructions

This document provides instructions for AI coding assistants working on this codebase.

## General instructions

- Ask questions if you need clarification.
- Search early; quote exact errors; prefer newer sources.
- Style: telegraph. Drop filler/grammar. Min tokens (global AGENTS + replies).
- Keep files shorter than ~500 LOC; split/refactor as needed.
- Always add tests when adding functionality.
- Always create or modify tests when fixing a bug.
- Always build the solution and run the tests after making changes. Fix all build errors and test failures before finishing your work.
- If the build fails because some process is running and locking the file, kill the process.
- Always update the README.md file so that it reflects the latest state of the project
- Always bump the version number in the `mvdmio.Database.PgSQL` and `mvmdio.Database.PgSQL.Tool` .csproj file when making changes that affect those projects. Follow semantic versioning principles (MAJOR.MINOR.PATCH):
  - Increment the MAJOR version when you make incompatible API changes.
  - Increment the MINOR version when you add functionality in a backward-compatible manner.
  - Increment the PATCH version when you make backward-compatible bug fixes.

## Project Overview

**mvdmio.Database.PgSQL** is a C# NuGet package that provides a wrapper around Dapper for PostgreSQL database interactions. It simplifies:

- Database connections and query execution via Dapper
- Transaction management
- Database migrations
- Bulk data operations (copy, upsert)
- Database management tasks (schema/table existence checks)

The package targets .NET 8.0, .NET 9.0, and .NET 10.0.

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
| `DatabaseConnection` | Main entry point for database operations. Provides access to Dapper, Management, and Bulk connectors. |
| `DapperDatabaseConnector` | Wraps Dapper methods with proper connection/transaction handling. |
| `ManagementDatabaseConnector` | Database management operations (TableExists, SchemaExists). |
| `BulkConnector` | High-performance bulk operations: Copy, InsertOrUpdate, InsertOrSkip. |
| `IDbMigration` | Interface for implementing database migrations. |
| `DatabaseMigrator` | Migration runner that tracks executed migrations. |

## Development Workflow

### Build and Test

```bash
# Build all projects
dotnet build

# Run all tests
dotnet test

# Format code
dotnet format
```

### Before Committing

1. Run `dotnet build` to ensure the project compiles
2. Run `dotnet test` to verify all tests pass
3. Run `dotnet format` to format code according to .editorconfig

### Test-Driven Development

Write tests before implementing features. Tests should cover all new code.

- **Unit tests:** `test/mvdmio.Database.PgSQL.Tests.Unit/`
- **Integration tests:** `test/mvdmio.Database.PgSQL.Tests.Integration/`

Integration tests use Testcontainers to spin up PostgreSQL in Docker. Each test runs in a transaction that is rolled back after completion.

## Code Style

This project uses a comprehensive `.editorconfig` file. Key conventions:

### Formatting

- **Indentation:** 3 spaces for C# files
- **Namespace style:** File-scoped (`namespace Foo;`)
- **Line endings:** CRLF
- **Final newline:** Required

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes, Methods, Properties | PascalCase | `DatabaseConnection` |
| Private fields | _camelCase | `_connection` |
| Parameters, Variables | camelCase | `connectionString` |
| Constants | UPPER_SNAKE_CASE | `DEFAULT_TIMEOUT` |
| Interfaces | IPrefix | `IDbMigration` |
| Generic parameters | TPrefix | `TEntity` |
| Async methods | Async suffix | `ExecuteAsync` |

### Code Preferences

- Use `var` when type is apparent
- Use file-scoped namespaces
- Use braces for multi-line statements
- Prefer pattern matching
- Use null propagation (`?.`) and null coalescing (`??`)

## Documentation

All public methods must have XML documentation comments:

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

## Testing Conventions

### Test Infrastructure

- Inherit from `TestBase` for integration tests with database access
- Each test runs in its own transaction (automatically rolled back)
- Use `AwesomeAssertions` for fluent assertions
- Test migrations are in `test/.../Fixture/Migrations/`

### Test Naming

Use descriptive names that explain what is being tested:

```csharp
public async Task QueryAsync_WithValidSql_ReturnsResults()
public async Task BulkCopy_WithEmptyTable_CompletesSuccessfully()
```

## Migration Conventions

Migration identifiers use timestamp format: `YYYYMMDDHHmm` (e.g., `202602161430`)

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

Key dependencies to be aware of:

- **Dapper:** Micro-ORM for database queries
- **Npgsql:** PostgreSQL ADO.NET provider
- **xunit.v3:** Test framework
- **Testcontainers.PostgreSql:** PostgreSQL container for integration tests
- **AwesomeAssertions:** Fluent assertions library
