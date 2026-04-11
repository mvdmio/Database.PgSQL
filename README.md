# mvdmio.Database.PgSQL

PostgreSQL tooling for .NET.

This repository contains two publishable packages:

- `mvdmio.Database.PgSQL`: a .NET library for PostgreSQL access, transactions, bulk operations, schema management, migrations, and generated repositories.
- `mvdmio.Database.PgSQL.Tool`: a `dotnet` global/local tool exposed as `db` for creating migrations, applying them, pulling schema files, and cleaning up obsolete migrations.

## Packages

### `mvdmio.Database.PgSQL`

Use this package in application code.

Install:

```bash
dotnet add package mvdmio.Database.PgSQL
```

What it includes:

- Dapper-based query and command execution for PostgreSQL
- Transaction helpers
- Bulk copy and upsert operations
- Schema inspection and schema export
- Migration runner
- Source-generated repositories for annotated table models

Streaming COPY sessions created with `BeginCopyAsync(...)` should be used with `await using`; failed writes now clean up the importer and release the connection through async disposal.

Quick example:

```csharp
using mvdmio.Database.PgSQL;

await using var db = new DatabaseConnection(
   "Host=localhost;Database=mydb;Username=postgres;Password=secret"
);

var users = await db.Dapper.QueryAsync<User>(
   "SELECT * FROM users WHERE active = :active",
   new Dictionary<string, object?> { ["active"] = true }
);
```

Package README:

- [`src/mvdmio.Database.PgSQL/README.md`](src/mvdmio.Database.PgSQL/README.md)

### `mvdmio.Database.PgSQL.Tool`

Use this package when you want a CLI for migration and schema workflows.

Install globally:

```bash
dotnet tool install --global mvdmio.Database.PgSQL.Tool
```

Or install locally to a tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install mvdmio.Database.PgSQL.Tool
```

After installation, the command is `db`.

Typical workflow:

```bash
db init
db migration create AddUsersTable
db migrate latest
db pull
```

Package README:

- [`src/mvdmio.Database.PgSQL.Tool/README.md`](src/mvdmio.Database.PgSQL.Tool/README.md)

## When To Use Which Package

- Use `mvdmio.Database.PgSQL` when writing application or service code that talks to PostgreSQL.
- Use `mvdmio.Database.PgSQL.Tool` when you want a CLI for migration authoring, migration execution, and schema export.
- Use both when your application uses the library and your team also wants a repeatable migration workflow from the command line.

## Repository Layout

```text
src/
  mvdmio.Database.PgSQL/        Main NuGet package
  mvdmio.Database.PgSQL.Tool/   CLI tool package
  mvdmio.Database.PgSQL.Analyzers/ Analyzer/source generator support
test/
  mvdmio.Database.PgSQL.Tests.Unit/
  mvdmio.Database.PgSQL.Tests.Integration/
  mvdmio.Database.PgSQL.Analyzers.Tests/
```

## Build And Test

```bash
dotnet format
dotnet build
dotnet test
```

## License

MIT. See [`LICENSE`](LICENSE).
