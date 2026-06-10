# Architecture & Layout

## Project structure

```
mvdmio.Database.PgSQL/
├── src/
│   ├── mvdmio.Database.PgSQL/              # Main library (NuGet package)
│   │   ├── Connectors/
│   │   │   ├── Bulk/                       # Bulk ops: Copy, InsertOrUpdate, InsertOrSkip
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

The analyzer project is referenced by the library as an analyzer and ships inside the package so its warnings run for consumers (e.g. migration class-name convention checks).

## Entry point

`DatabaseConnection` is the main entry point. It exposes:

- `Dapper` — `DapperDatabaseConnector`: query/execute with connection + transaction handling.
- `Management` — `ManagementDatabaseConnector`: `TableExistsAsync`, `SchemaExistsAsync`, schema extraction.
- `Bulk` — `BulkConnector`: high-performance `Copy`, `InsertOrUpdate`, `InsertOrSkip`.

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
