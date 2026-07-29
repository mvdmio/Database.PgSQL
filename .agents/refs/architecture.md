# Architecture & Layout

## Project structure

```
mvdmio.Database.PgSQL/
├── src/
│   ├── mvdmio.Database.PgSQL/              # Main library (NuGet package)
│   │   ├── Connectors/
│   │   │   ├── Bulk/                       # Bulk ops: Copy, InsertOrUpdate, InsertOrSkip
│   │   │   ├── Linq/                       # Query surface: linq2db adapter, mapping schema, queryable decorator
│   │   │   ├── Schema/                     # Schema extraction / export (SchemaExtractor)
│   │   │   ├── DapperDatabaseConnector.cs  # Dapper wrapper
│   │   │   └── ManagementDatabaseConnector.cs
│   │   ├── Dapper/                         # Dapper configuration & type handlers
│   │   ├── Exceptions/                     # Custom exceptions
│   │   ├── Extensions/
│   │   ├── Migrations/                     # Migration framework
│   │   │   ├── Interfaces/                 # IDbMigration, IDatabaseMigrator
│   │   │   ├── MigrationRetrievers/        # IMigrationRetriever + reflection impl
│   │   │   ├── Models/                     # ExecutedMigrationModel, SchemaFileMigrationInfo
│   │   │   ├── DatabaseMigrator.cs         # Migration runner
│   │   │   ├── EmbeddedSchemaDiscovery.cs  # Finds embedded schema.sql resources
│   │   │   └── SchemaFileParser.cs         # Parses schema-file header version
│   │   └── DatabaseConnection.cs           # Main entry point
│   ├── mvdmio.Database.PgSQL.Tool/         # CLI tool — `dotnet tool` command `db`
│   └── mvdmio.Database.PgSQL.Analyzers/    # Roslyn analyzer (netstandard2.0, ships in the package)
├── test/
│   ├── mvdmio.Database.PgSQL.Tests.Unit/
│   ├── mvdmio.Database.PgSQL.Tests.Integration/
│   ├── mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema/  # 2nd assembly for multi-assembly / multi-schema tests
│   ├── mvdmio.Database.PgSQL.Tests.Integration.OData/            # OData conformance suite over the query surface
│   ├── mvdmio.Database.PgSQL.Tests.Packaging/                    # Installs the packed .nupkg into a scaffolded project and runs it
│   └── mvdmio.Database.PgSQL.Analyzers.Tests/
├── docs/adr/                               # Architecture decision records
├── CONTEXT.md                              # Domain glossary
├── Directory.Build.props                   # Shared version (PgSqlVersion) + repo metadata
├── .github/workflows/publish-nuget.yml     # CI/CD
└── README.md
```

## Two shipped packages

1. **`mvdmio.Database.PgSQL`** — the library. Wraps Dapper for PostgreSQL: connections, transactions, queries, bulk ops, management tasks, and the migration framework.
2. **`mvdmio.Database.PgSQL.Tool`** — a `dotnet tool` (command name `db`) for migration operations: init config, scaffold migrations, run migrations, pull schemas, clean up obsolete migration files, copy data.

The analyzer project is referenced by the library as an analyzer and is packed into `analyzers/dotnet/cs` inside `mvdmio.Database.PgSQL`, so both its warnings and its table-repository source generator run for consumers. It is never published on its own: generated code calls into the library's own API, so the two version-lock, and two packages would make skew expressible. Its `Microsoft.CodeAnalysis.CSharp` version is a floor rather than a preference — an analyzer referencing a newer Roslyn than the host compiler is skipped with a warning instead of failing, which looks exactly like the generator not existing.

## Entry point

`DatabaseConnection` is the main entry point. It exposes:

- `Dapper` — `DapperDatabaseConnector`: query/execute with connection + transaction handling.
- `Management` — `ManagementDatabaseConnector`: `TableExistsAsync`, `SchemaExistsAsync`, schema extraction.
- `Bulk` — `BulkConnector`: high-performance `Copy`, `InsertOrUpdate`, `InsertOrSkip`.
- `Linq` — `LinqDatabaseConnector`: the query surface behind a generated repository's `Query()`. Owns a non-owning
  linq2db context rebuilt whenever the connection or ambient transaction changes, plus the process-wide mapping schema
  and its customization hook. Read [ADR 0004](../../docs/adr/0004-linq2db-as-the-queryable-provider.md) before
  changing it.

## Migration framework

- Migrations implement `IDbMigration`; `Identifier` (a `YYYYMMDDHHmm` timestamp) and `Name` default to values parsed from the class name (`_{identifier}_{name}`).
- `DatabaseMigrator` runs pending migrations, tracked in the `mvdmio.migrations` table, serialized across instances by a session-scoped advisory lock (ADR 0001).
- Schema-first bootstrap: an empty database can be seeded from an embedded `schema.sql` whose header records a baseline migration version; only migrations past the baseline then run.

## Important files

| File | Purpose |
|------|---------|
| `src/mvdmio.Database.PgSQL/DatabaseConnection.cs` | Main entry point |
| `src/mvdmio.Database.PgSQL/Migrations/DatabaseMigrator.cs` | Migration runner & orchestration |
| `src/mvdmio.Database.PgSQL/Migrations/Interfaces/IDbMigration.cs` | Migration interface |
| `src/mvdmio.Database.PgSQL/Connectors/Schema/SchemaExtractor.cs` | Schema script generation (header + DDL) |
| `Directory.Build.props` | Single source of the package version |
