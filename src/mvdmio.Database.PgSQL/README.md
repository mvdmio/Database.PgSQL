# mvdmio.Database.PgSQL

PostgreSQL access for .NET applications.

The package combines Dapper and Npgsql with a higher-level API for common PostgreSQL workflows.

## Installation

```bash
dotnet add package mvdmio.Database.PgSQL
```

Targets `net8.0`, `net9.0`, and `net10.0`.

## What You Can Do With It

- Run SQL queries and commands through `db.Dapper`
- Execute work inside transactions
- Bulk insert or upsert rows
- Check schema and table existence
- Export the current database schema
- Limit schema export to selected PostgreSQL schemas when using the companion CLI configuration
- Preserve identity columns and stored generated columns during schema export
- Run migrations from application code
- Generate repositories from annotated table models
- Auto-embed `Schemas/**/*.sql` files for direct and transitive project references

## Quick Start

```csharp
using mvdmio.Database.PgSQL;

await using var db = new DatabaseConnection(
   "Host=localhost;Database=mydb;Username=postgres;Password=secret"
);

var users = await db.Dapper.QueryAsync<User>(
   "SELECT * FROM users WHERE active = :active",
   new Dictionary<string, object?> { ["active"] = true }
);

await db.Dapper.ExecuteAsync(
   "INSERT INTO users (name, email) VALUES (:name, :email)",
   new Dictionary<string, object?>
   {
      ["name"] = "Alice",
      ["email"] = "alice@example.com"
   }
);
```

## Common Usage

### Transactions

```csharp
await db.InTransactionAsync(async () =>
{
   await db.Dapper.ExecuteAsync(
      "INSERT INTO orders (customer_id, total) VALUES (:customerId, :total)",
      new Dictionary<string, object?>
      {
         ["customerId"] = 42,
         ["total"] = 99.95m
      }
   );
});
```

### Bulk Operations

```csharp
var mapping = new Dictionary<string, Func<Product, DbValue>>
{
   ["sku"] = x => x.Sku,
   ["name"] = x => x.Name,
   ["price"] = x => x.Price
};

await db.Bulk.CopyAsync("products", products, mapping);
```

For streaming COPY sessions, prefer `await using` so failed writes still dispose the importer and release the connection:

```csharp
await using var session = await db.Bulk.BeginCopyAsync<Product>("products", mapping);

foreach (var product in products)
   await session.WriteAsync(product);

await session.CompleteAsync();
```

### Migrations In Code

```csharp
using mvdmio.Database.PgSQL.Migrations;

var migrator = new DatabaseMigrator(db, logger, typeof(Program).Assembly);
await migrator.MigrateDatabaseToLatestAsync();
```

`DatabaseMigrator` requires an `ILogger<DatabaseMigrator>` (from `Microsoft.Extensions.Logging.Abstractions`) so migration warnings and diagnostics flow into your application's logging. Pass `NullLogger<DatabaseMigrator>.Instance` if you do not use logging.

#### Migration scopes

Every migration belongs to a **scope** — the logical timeline it is tracked within. The scope defaults to the simple name of the assembly that declares the migration, so multi-assembly setups work without changing any migration. The migrations table records the scope of each executed migration, and a migration runs when its identifier is ahead of the highest executed identifier *within its own scope*. Scopes advance independently: two assemblies migrating the same database can never suppress each other's migrations, even when their timestamps interleave. Uniqueness is enforced per scope via a `UNIQUE (scope, identifier)` index, so the same identifier can exist in two different scopes.

Override `IDbMigration.Scope` to pin a stable scope:

```csharp
public class _202602161430_AddUsersTable : IDbMigration
{
   public string Scope => "MyApp.Data"; // survives an assembly rename

   public async Task UpAsync(DatabaseConnection db) { /* ... */ }
}
```

> **Renaming an assembly without pinning the scope forks the migration history**: the renamed assembly becomes a new scope with no watermark, and every one of its migrations runs again. Override `Scope` (or keep the assembly name stable) when renaming.

#### Upgrading from 0.27 or earlier

The first run of this version upgrades the migrations table in place: it adds a nullable `scope` column, drops the legacy `PRIMARY KEY (identifier)`, and adds a named `UNIQUE (scope, identifier)` index. Existing rows are attributed to their scope by a **temporary backfill** that matches rows to discovered migrations by identifier; rows no discovered migration claims stay scope-less, are excluded from every scope's watermark, and produce a logged warning so you can set their scope manually. The backfill is removed in the next major version, when `scope` becomes `NOT NULL` and the unique index is promoted to `PRIMARY KEY (scope, identifier)`.

> **One state cannot be healed automatically:** a database that was bootstrapped schema-first from **multiple assemblies** under an older version recorded only a single baseline row (the highest header version across all schemas). The backfill can attribute that row to one scope only — the other scopes have no baseline, so their folded-in migrations would be selected again and fail loudly on the schema-created objects. If you upgrade such a database, insert the missing scopes' baseline rows into `mvdmio.migrations` manually before running migrations.

Source-breaking changes in this version: all `DatabaseMigrator` constructors require an `ILogger<DatabaseMigrator>`; `ExecutedMigrationModel` and `SchemaFileMigrationInfo` gained a nullable `Scope`; `SchemaFileParser.ParseMigrationVersion` and `SchemaExtractor.GetCurrentMigrationVersionAsync` return collections (one entry per scope).

#### Concurrent startup

`MigrateDatabaseToLatestAsync` and `MigrateDatabaseToAsync` are safe to call from multiple application instances starting at the same time (rolling deploys, autoscaling, multi-pod startup). The runner serializes the entire migration run with a session-scoped PostgreSQL advisory lock: only one instance migrates at a time, every other instance blocks until it finishes, then acquires the lock, re-reads the current state, finds nothing left to do, and continues. This is always on, requires no configuration, and is released automatically if the migrating instance crashes.

> **PgBouncer caveat:** session-scoped advisory locks do not work behind PgBouncer in **transaction-pooling** mode, because successive statements may land on different backends. Run migrations against a **direct connection** or a **session-pooled** connection. A consumer-configured server-side `statement_timeout` can also cancel the blocking lock acquisition.

### Embedded Schema Files

When a project references `mvdmio.Database.PgSQL` directly, or references another project that does, any `Schemas/**/*.sql` files in that project are automatically included as embedded resources.

This works through the package's `build` and `buildTransitive` MSBuild props files.

When `DatabaseMigrator` is constructed with multiple assemblies and the target database is empty, **every** assembly that contains an embedded `schema.sql` (or environment-specific `schema.{env}.sql`) has its schema applied, in the order the assemblies are passed to the constructor. Assemblies without a matching schema resource are silently skipped. All schemas run in a single transaction, and the migrations table is pre-created so that schema files using `CREATE TABLE IF NOT EXISTS "mvdmio"."migrations"` do not conflict.

Generated schema files carry one `-- Migration version: <id> (<name>) [<scope>]` header line per scope. One baseline row is recorded in `mvdmio.migrations` per scope (the highest header version for that scope across all applied schemas), so migrations folded into one assembly's schema are not re-run while another assembly bootstraps alongside it. Older scope-less headers (`-- Migration version: <id> (<name>)`) are still read; they record a scope-less baseline that the backfill attributes on the same run, so you do not have to regenerate existing `schema.sql` files when upgrading.

If the database already contains migrations, no schema files are applied. The empty-database check is global (not per scope): schema-first bootstrap works for the first scope to touch a fresh database, or for multiple assemblies bootstrapped by a single `DatabaseMigrator` instance — a second, separate schema-first migrator instance against an already-populated database is not supported and falls back to running migrations. When `MigrateDatabaseToAsync(targetIdentifier)` is used and any discovered schema's header version exceeds the target, the entire schema-first bootstrap is skipped (applying a subset would leave gaps that later migrations cannot fill).

### Generated Repositories

```csharp
using mvdmio.Database.PgSQL.Attributes;

[Table("public.users")]
public partial class UserTable
{
   [PrimaryKey]
   [Generated]
   public long UserId { get; set; }

   [Unique]
   public string UserName { get; set; } = string.Empty;
}
```

From that model, the package generates repository types and CRUD command/data types you can use in application code.

## CLI Tool

If you want a command-line workflow for migrations and schema files, install the companion tool:

```bash
dotnet tool install --global mvdmio.Database.PgSQL.Tool
```

See [`../mvdmio.Database.PgSQL.Tool/README.md`](../mvdmio.Database.PgSQL.Tool/README.md).

## License

MIT. See [`../../LICENSE`](../../LICENSE).
