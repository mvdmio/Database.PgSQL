# mvdmio.Database.PgSQL

PostgreSQL access for .NET applications.

The package combines Dapper and Npgsql with a higher-level API for common PostgreSQL workflows.

## Installation

```bash
dotnet add package mvdmio.Database.PgSQL
```

Targets `net8.0`, `net9.0`, and `net10.0`.

## What You Can Do With It

- [Open connections](#connections) directly or through a pooling factory
- [Run SQL queries and commands](#queries-and-commands) through `db.Dapper`
- [Execute work inside transactions](#transactions)
- [Bulk copy, upsert, and insert-or-skip](#bulk-operations) large batches of rows
- [Generate repositories](#generated-repositories) from annotated table models
- [Compose queries at runtime](#composable-queries) over an `IQueryable<T>` that translates to SQL
- [Run migrations](#migrations) from application code — tracked per scope and safe under concurrent startup
- [Inspect and export the database schema](#schema-inspection-and-export)
- [Wait for `NOTIFY` messages](#listennotify) on a channel

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

## Connections

`DatabaseConnection` is the entry point. It opens and closes the underlying connection around each operation, so you
do not have to manage that yourself. It exposes four connectors:

| Property        | Use for                                                     |
|-----------------|-------------------------------------------------------------|
| `db.Dapper`     | Queries and commands                                        |
| `db.Bulk`       | `COPY`-based bulk inserts, upserts, and temp tables          |
| `db.Management` | Schema/table existence checks, schema inspection and export  |
| `db.Info`       | `Host`, `Port`, and `Database` of the current connection     |

Create one from a connection string, or from an `NpgsqlDataSource` you own:

```csharp
// Owns its data source: disposing the connection disposes the data source too.
await using var db = new DatabaseConnection(connectionString);

// Configure Npgsql while the data source is built.
await using var configured = new DatabaseConnection(connectionString, builder => builder.EnableDynamicJson());

// Borrows a data source you own: disposing the connection leaves the data source alone.
await using var borrowed = new DatabaseConnection(dataSource);
```

A `DatabaseConnection` wraps a single connection, so use one per unit of work rather than sharing one across
concurrent operations. `Open()`/`Close()` (and their async overloads) let you hold the connection open across several
operations when you want to avoid the open/close cycle per call.

### Connection Factory

`DatabaseConnectionFactory` caches one `NpgsqlDataSource` per connection string, so pooling is shared by every
connection it hands out. Build short-lived connections from it:

```csharp
await using var factory = new DatabaseConnectionFactory();

await using var db = factory.BuildConnection(connectionString);

// Or take the cached data source itself, to hand to Npgsql directly.
var dataSource = factory.BuildDataSource(connectionString);
```

Data sources from the factory are built with dynamic JSON serialization enabled and with Npgsql's `IncludeErrorDetail`
and `LogParameters` turned on, which the plain `new DatabaseConnection(connectionString)` constructor does not do.
Both `BuildConnection` and `BuildDataSource` take an optional `Action<NpgsqlDataSourceBuilder>` to configure the rest.

Connections from the factory do not dispose the shared data source, so disposing one does not affect the others.
Dispose the factory only after everything using its connections has finished.

### Dependency Injection

```csharp
using mvdmio.Database.PgSQL;

services.AddDatabase(); // registers DatabaseConnectionFactory as a singleton

services.AddScoped(sp => sp.GetRequiredService<DatabaseConnectionFactory>()
   .BuildConnection(configuration.GetConnectionString("Database")!));
```

`AddDatabase()` registers the factory only — register `DatabaseConnection` yourself, as above, so you control its
lifetime and connection string. A scoped registration gives each request its own connection while all of them share
the factory's pool.

If your models use enums, map them once at startup (see [Type Handling](#type-handling)):

```csharp
services.AddEnumDapperTypeHandlers(typeof(Program).Assembly);
```

## Queries and Commands

`db.Dapper` mirrors Dapper's API. Parameters are passed as an `IDictionary<string, object?>` and referenced in SQL
with a leading colon:

```csharp
var user = await db.Dapper.QuerySingleOrDefaultAsync<User>(
   """
   SELECT user_id, user_name, created_at
   FROM users
   WHERE user_name = :userName
   """,
   new Dictionary<string, object?> { ["userName"] = "alice" }
);
```

Available methods, each with a sync and an async overload:

- `Execute` — run a command, returns the number of affected rows
- `ExecuteScalar` / `ExecuteScalar<T>` — read a single value
- `ExecuteReader` — get a raw `IDataReader` / `DbDataReader`
- `Query` / `Query<T>` — read many rows, as `dynamic` or mapped to `T`
- `Query<TFirst, TSecond, …, TReturn>` — multi-mapping for joins, up to six input types
- `QueryFirst<T>` / `QueryFirstOrDefault<T>` / `QuerySingle<T>` / `QuerySingleOrDefault<T>`
- `QueryMultiple<T>` — read several result sets from one command, through a `Func<SqlMapper.GridReader, T>` you supply

All of them accept an optional `commandTimeout`, and the async overloads take a `CancellationToken`:

```csharp
await db.Dapper.ExecuteAsync(sql, parameters, commandTimeout: TimeSpan.FromMinutes(5), ct: ct);
```

### Column Mapping

`snake_case` columns map to `PascalCase` properties automatically, so `first_name` fills `FirstName` without any
attribute or configuration.

### Explicit Parameter Types

When PostgreSQL needs the parameter type spelled out — arrays, `jsonb`, ambiguous casts — wrap the value in a
`TypedQueryParameter`:

```csharp
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using NpgsqlTypes;

await db.Dapper.ExecuteAsync(
   "UPDATE users SET tags = :tags WHERE user_id = :userId",
   new Dictionary<string, object?>
   {
      ["tags"] = new TypedQueryParameter(new[] { "admin", "beta" }, NpgsqlDbType.Array | NpgsqlDbType.Text),
      ["userId"] = 42
   }
);
```

### Errors

A failed query throws `QueryException`, which carries the offending SQL in its `Sql` property and includes it in
`ToString()`. Migration failures throw `MigrationException`. A composed query that cannot be turned into SQL throws
`QueryTranslationException` — there is no SQL yet at that point. All three derive from `DatabaseException`, so
`catch (DatabaseException)` covers them. Invalid arguments and misuse still surface as the usual
`ArgumentException`/`InvalidOperationException`, and bulk `COPY` failures can surface Npgsql's own
`PostgresException`.

## Transactions

The simplest form runs a delegate in a transaction, committing on success and rolling back on exception:

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

Use the generic overload to return a value, or `InTransaction(Action)` to run synchronously:

```csharp
var orderId = await db.InTransactionAsync(async () =>
   await db.Dapper.ExecuteScalarAsync<long>(
      "INSERT INTO orders (total) VALUES (:total) RETURNING order_id",
      new Dictionary<string, object?> { ["total"] = 99.95m }
   )
);
```

Transactions do not nest: an `InTransactionAsync` call inside another one joins the outer transaction, and only the
outermost call commits or rolls back. Every operation on the same `DatabaseConnection` — including
[generated repositories](#generated-repositories) — automatically runs inside whatever transaction that connection
has open.

For full control, or to pick an isolation level, drive the transaction yourself:

```csharp
using System.Data;

await db.BeginTransactionAsync(IsolationLevel.Serializable, ct);

try
{
   // ... work ...
   await db.CommitTransactionAsync(ct);
}
catch
{
   await db.RollbackTransactionAsync(ct);
   throw;
}
```

`BeginTransactionAsync` returns `true` when it started a transaction and `false` when one was already open.

## Bulk Operations

Bulk methods take a column-to-value mapping, so your model does not have to mirror the table:

```csharp
var mapping = new Dictionary<string, Func<Product, DbValue>>
{
   ["sku"] = x => x.Sku,
   ["name"] = x => x.Name,
   ["price"] = x => new DbValue(x.Price, NpgsqlDbType.Numeric)
};

await db.Bulk.CopyAsync("products", products, mapping);
```

`DbValue` converts implicitly from `string`, `bool`, the integer and floating-point types, `DateTime`,
`DateTimeOffset`, `DateOnly`, and `TimeOnly`. For every other type — `decimal`, `Guid`, arrays, `jsonb` — construct it
with the PostgreSQL type spelled out:

```csharp
["metadata"] = x => new DbValue(x.Metadata, NpgsqlDbType.Jsonb)
```

### Streaming Copy

For batches you do not want to hold in memory, open a session. Use `await using` so a failed write still disposes
the importer and releases the connection:

```csharp
await using var session = await db.Bulk.BeginCopyAsync<Product>("products", mapping);

foreach (var product in products)
   await session.WriteAsync(product);

await session.CompleteAsync();
```

### Upsert and Insert-Or-Skip

`InsertOrUpdateAsync` stages the batch in a temp table and merges it into the target table, returning each affected
row with a flag for whether it was inserted or updated:

```csharp
var results = await db.Bulk.InsertOrUpdateAsync("products", "sku", products, mapping);

var inserted = results.Count(x => x.IsInserted);
var updated = results.Count(x => x.IsUpdated);
```

Rows whose values are identical to what is already stored are left alone and do not appear in the result, so the
returned count can be lower than the number of items passed in.

Pass a `string[]` for a composite conflict target, or an `UpsertConfiguration` to also supply the predicate of a
partial unique index:

```csharp
await db.Bulk.InsertOrUpdateAsync(
   "products",
   new UpsertConfiguration
   {
      OnConflictColumns = ["sku", "tenant_id"],
      OnConflictWhereClause = "deleted_at IS NULL"
   },
   products,
   mapping
);
```

`InsertOrSkipAsync` takes the same arguments but leaves conflicting rows untouched and returns only the rows it
inserted.

### Temp Tables and Cross-Connection Copy

```csharp
// Stage rows in a temp table and get its generated name, to run your own SQL against.
var tempTable = await db.Bulk.CopyToTempTableAsync(products, mapping);

// Stream a table straight from another database into this one.
var bytesCopied = await db.Bulk.CopyFromAsync(sourceDb, "public", "products", ["sku", "name", "price"]);
```

`CopyToTempTableAsync` infers column types from the first item, so it needs at least one row. `CopyFromAsync` requires
the table to exist on both sides with the same columns in the same order, and does not truncate the destination —
that is the caller's job.

## Generated Repositories

Annotate a table model and the package generates a typed repository for it at build time: no runtime reflection, no
hand-written CRUD SQL.

```csharp
using mvdmio.Database.PgSQL.Attributes;

namespace MyApp.Data.Users;

[Table("public.users")]
public partial class UserTable
{
   [PrimaryKey]
   [Generated]
   public long UserId { get; set; }

   [Unique]
   public string UserName { get; set; } = string.Empty;

   [Column("first_name")]
   public string FirstName { get; set; } = string.Empty;

   public DateTimeOffset? LastLoginAt { get; set; }
}
```

### Using the Repository

```csharp
var repository = new UserRepository(db);

// INSERT ... RETURNING, so the result carries database-generated values.
var created = await repository.CreateAsync(new CreateUserCommand
{
   UserName = "alice",
   FirstName = "Alice"
}, ct);

var all = await repository.GetAllAsync(ct);                             // IEnumerable<UserData>
var byId = await repository.GetByUserIdAsync(created.UserId, ct);        // UserData?
var byName = await repository.GetByUserNameAsync("alice", ct);           // UserData?

var updated = await repository.UpdateAsync(new UpdateUserCommand
{
   UserId = created.UserId,
   UserName = "alice",
   FirstName = "Alicia",
   LastLoginAt = DateTimeOffset.UtcNow
}, ct);

var deleted = await repository.DeleteByUserIdAsync(created.UserId, ct);  // false when no row matched
```

A repository takes a `DatabaseConnection` and runs all of its SQL through it, so it joins whatever transaction that
connection has open.

### What Gets Generated

For `UserTable`, five types are generated in the same namespace — `public` when the table class is public, `internal`
otherwise:

| Type                | Contains                                                      |
|---------------------|---------------------------------------------------------------|
| `UserData`          | Every mapped property — the type all reads and writes return   |
| `CreateUserCommand` | Every property except `[Generated]` ones                       |
| `UpdateUserCommand` | The primary key, plus every other non-`[Generated]` property    |
| `IUserRepository`   | The repository interface                                       |
| `UserRepository`    | The implementation                                             |

The class name minus its `Table` suffix supplies these names, so `ProductTable` produces `ProductData`,
`CreateProductCommand`, `IProductRepository`, and so on. All five are `partial`, so you can add members to them from
your own files.

The repository exposes:

- `CreateAsync(Create…Command, CancellationToken)` → the created row
- `GetAllAsync(CancellationToken)` → every row
- `GetBy{Property}Async(value, CancellationToken)` → the matching row or `null`; one method per primary key and
  `[Unique]` property
- `UpdateAsync(Update…Command, CancellationToken)` → the updated row, matched on the primary key
- `DeleteBy{Property}Async(value, CancellationToken)` → `true` when a row was deleted; one method per primary key and
  `[Unique]` property
- `Query(TimeSpan? commandTimeout = null)` → an `IQueryable<…Data>` you compose against — see
  [Composable Queries](#composable-queries)

### Composable Queries

`Query()` returns a deferred `IQueryable<…Data>` over the table. Nothing runs until you materialize it, and what you
compose translates to SQL rather than being filtered in memory:

```csharp
var repository = new UserRepository(db);

var page = await repository.Query()
   .Where(x => x.FirstName == firstName && x.LastLoginAt > cutoff)
   .OrderByDescending(x => x.LastLoginAt)
   .ThenBy(x => x.UserName)
   .Skip(20)
   .Take(20)
   .ToListAsync(ct);
```

Values that come from local variables become SQL parameters, not inlined literals, so PostgreSQL can reuse the query
plan.

Because it is the framework's own `IQueryable<T>`, you can hand it to anything that consumes one — including an
ASP.NET Core OData action, where `[EnableQuery]` composes `$filter`, `$orderby`, `$top` and `$skip` onto it and the
database does the work:

```csharp
[HttpGet]
[EnableQuery]
public IQueryable<UserData> Get() => _repository.Query();
```

`Query()` is declared on the generated interface as well as the class, so a test can hand a caller a fake:

```csharp
public IQueryable<UserData> Query(TimeSpan? commandTimeout = null) => _users.AsQueryable();
```

#### Awaiting a query

Materialization methods live in `mvdmio.Database.PgSQL`, so a `using mvdmio.Database.PgSQL;` is all you need:

| Method                                  | Returns                                          |
|-----------------------------------------|--------------------------------------------------|
| `ToListAsync(ct)`                       | `List<T>`                                        |
| `FirstAsync(ct)` / `FirstOrDefaultAsync(ct)` | the first row; `FirstAsync` throws when there is none |
| `SingleAsync(ct)` / `SingleOrDefaultAsync(ct)` | the only row                               |
| `CountAsync(ct)` / `LongCountAsync(ct)` | the row count                                    |
| `AnyAsync(ct)`                          | whether any row matched                          |

The synchronous LINQ operators — `ToList()`, `First()`, `Single()`, `Count()`, `Any()` — work too. The queryable is
also an `IAsyncEnumerable<T>`, so frameworks that detect asynchronous enumeration use it without knowing about this
package:

```csharp
await foreach (var user in (IAsyncEnumerable<UserData>)repository.Query().Where(x => x.LastLoginAt != null))
{
   // …
}
```

#### What translates

Equality, inequality, ordering comparisons, and `&&`/`||` combinations; `OrderBy`/`OrderByDescending` with
`ThenBy`/`ThenByDescending`; `Skip` and `Take`; `Count`, `LongCount` and `Any`; and the single-row operators. Null
comparison follows C#, not SQL: `x.Nickname != "bobby"` returns the rows where `nickname` is null, the way the C# you
wrote reads.

An expression that cannot be translated throws `QueryTranslationException` — a client error, not a server error. A
query that reaches the database and fails there throws the usual `QueryException` with the SQL attached.

#### Connections, transactions, and timeouts

A query executes against the connection and transaction that are current *when it runs*, not when it was composed, so
composing before opening a transaction and enumerating inside it reads your own writes. Composing does not touch the
database. Executing opens the connection if it is not open yet, and leaves it open — a queryable can be enumerated
again at any time, so there is no point at which the query surface could close it for you. It closes with the
`DatabaseConnection`, or when you call `Close()` yourself. Enumerating a query whose `DatabaseConnection` has been
disposed throws `ObjectDisposedException`.

SQL is generated for the newest PostgreSQL dialect the package knows about. Override it per connection when you target
an older server:

```csharp
db.Linq.Dialect = PostgresDialect.V13;
```

Pass `commandTimeout` to bound a query you know may be expensive. There is no default — like every other adapter here,
the timeout is yours to set:

```csharp
var report = await repository.Query().Where(x => x.LastLoginAt < cutoff).ToListAsync(ct);
var slowReport = await repository.Query(TimeSpan.FromMinutes(2)).ToListAsync(ct);
```

The query surface is read-only. Mutation stays on the generated commands and on `db.Dapper` and `db.Bulk`. It also
applies no limits of its own: no page cap, no row ceiling, and no per-column restriction on what may be filtered or
sorted. If you expose a query surface to callers you do not trust — an OData endpoint, for instance — constraining it
is yours to do.

Cross-table queries are not supported: a query covers the one table its table model describes.

### Attributes

| Attribute       | Effect                                                                                                |
|-----------------|-------------------------------------------------------------------------------------------------------|
| `[Table("…")]`  | Marks the class and names the table. Takes `schema.table`, or `table` for the `public` schema           |
| `[PrimaryKey]`  | Marks the key used by `UpdateAsync`, `GetBy…`, and `DeleteBy…`. Exactly one property must carry it      |
| `[Unique]`      | Adds a `GetBy…`/`DeleteBy…` pair for that property                                                     |
| `[Column("…")]` | Overrides the column name. Without it, the property name is converted to `snake_case`                    |
| `[Generated]`   | The database produces the value (identity, computed, or defaulted): it is read back but never written   |

The `snake_case` conversion inserts an underscore before every uppercase letter, so `UserId` becomes `user_id` but
`UserID` becomes `user_i_d` — name the column explicitly with `[Column]` when the property contains an acronym.

### Requirements

The generator reports a build error when a table model does not satisfy these:

- The class is `partial`
- `[Table]` names a valid `table` or `schema.table`
- Exactly one property is marked `[PrimaryKey]`
- At least one property is neither the primary key nor `[Generated]`, so there is something to update
- Mapped properties are public, non-static, not indexers, and have both a public getter and setter
- No two properties map to the same column, and no two lookup properties yield the same method name

A separate analyzer warns — rather than errors — when the class name does not end with `Table`, since the suffix only
determines the generated names. See [Build-Time Diagnostics](#build-time-diagnostics) for the codes.

### Dependency Injection

Every assembly containing table models also gets an `IServiceCollection` extension named after it — assembly
`MyApp.Data` produces `AddMyAppData()` in namespace `MyApp.Data`. It calls `AddDatabase()` and registers each
generated repository as scoped against its interface, leaving any registration you made yourself in place:

```csharp
using MyApp.Data;

services.AddMyAppData();

// Register the connection itself as well:
services.AddScoped(sp => sp.GetRequiredService<DatabaseConnectionFactory>()
   .BuildConnection(configuration.GetConnectionString("Database")!));
```

Then inject `IUserRepository` wherever you need it.

## Migrations

A migration is a class implementing `IDbMigration`, named `_{YYYYMMDDHHmm}_{Name}`. The timestamp in the name is the
migration's identifier and orders it against the others; the remainder is its display name.

```csharp
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

public class _202602161430_AddUsersTable : IDbMigration
{
   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE TABLE public.users (
            user_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            user_name TEXT NOT NULL UNIQUE
         )
         """
      );
   }
}
```

Each migration runs in its own transaction, so a failing migration rolls back on its own. The companion
[CLI tool](#cli-tool) scaffolds these files with `db migration create <name>`.

### Running Migrations

```csharp
using mvdmio.Database.PgSQL.Migrations;

var migrator = new DatabaseMigrator(db, loggerFactory, typeof(Program).Assembly);
await migrator.MigrateDatabaseToLatestAsync();
```

Pass every assembly that holds migrations or embedded schema files. The `ILoggerFactory` (from the
`Microsoft.Extensions.Logging.Abstractions` package) routes migration warnings and diagnostics into your application's
logging — pass `NullLoggerFactory.Instance` if you do not use logging. Other constructors take an environment name,
to bind [schema discovery](#embedded-schema-files) to an environment, and an `IMigrationRetriever`, to supply
migrations from somewhere other than assembly reflection.

Besides `MigrateDatabaseToLatestAsync`, the migrator offers:

- `MigrateDatabaseToAsync(identifier)` — stop at a specific identifier instead of running everything
- `RetrieveAlreadyExecutedMigrationsAsync()` — read the migration history
- `RunAsync(migration)` — run a single migration and record it
- `IsDatabaseEmptyAsync()` — check whether any migration has ever been applied

### Migration Scopes

Every migration belongs to a **scope** — the logical timeline it is tracked within. The scope defaults to the simple
name of the assembly that declares the migration, so multi-assembly setups work without changing any migration. The
migrations table records the scope of each executed migration, and a migration runs when its identifier is ahead of
the highest executed identifier *within its own scope*. Scopes advance independently: two assemblies migrating the
same database can never suppress each other's migrations, even when their timestamps interleave. Uniqueness is
enforced per scope, so the same identifier can exist in two different scopes.

Override `IDbMigration.Scope` to pin a stable scope:

```csharp
public class _202602161430_AddUsersTable : IDbMigration
{
   public string Scope => "MyApp.Data"; // survives an assembly rename

   public async Task UpAsync(DatabaseConnection db) { /* ... */ }
}
```

> **Renaming an assembly without pinning the scope forks the migration history**: the renamed assembly becomes a new
> scope with no watermark, and every one of its migrations runs again. Override `Scope` (or keep the assembly name
> stable) when renaming.

### Concurrent Startup

`MigrateDatabaseToLatestAsync` and `MigrateDatabaseToAsync` are safe to call from multiple application instances
starting at the same time (rolling deploys, autoscaling, multi-pod startup). The runner serializes the entire
migration run with a session-scoped PostgreSQL advisory lock: only one instance migrates at a time, every other
instance blocks until it finishes, then acquires the lock, re-reads the current state, finds nothing left to do, and
continues. This is always on, needs no configuration, and is released automatically if the migrating instance
crashes.

> **PgBouncer caveat:** session-scoped advisory locks do not work behind PgBouncer in **transaction-pooling** mode,
> because successive statements may land on different backends. Run migrations against a **direct connection** or a
> **session-pooled** connection. A server-side `statement_timeout` can also cancel the blocking lock acquisition.

### Embedded Schema Files

When a project references `mvdmio.Database.PgSQL` directly, or references another project that does, any
`Schemas/**/*.sql` files in that project are automatically embedded into the assembly. This lets a fresh database be
created from one schema file instead of replaying years of migrations. Generate those files with the
[CLI tool](#cli-tool)'s `db pull`.

When `DatabaseMigrator` is constructed with multiple assemblies and the target database is empty, **every** assembly
that contains an embedded `schema.sql` (or environment-specific `schema.{env}.sql`) has its schema applied, in the
order the assemblies are passed to the constructor. Assemblies without a matching schema resource are skipped. All
schemas run in a single transaction.

A schema file's `-- Migration version: <id> (<name>) [<scope>]` header lines — one per scope — establish the
baseline, so migrations already folded into a schema file are not re-run while another assembly bootstraps alongside
it. Header lines without a `[<scope>]` part are accepted too.

A schema file may only establish a baseline for scopes its own assembly **vouches for**: the scopes of migrations
discovered from that assembly, plus the assembly's simple name. Header lines naming any other scope — typically a
schema pulled from a database that several applications share, without `scopes` declared in
`.mvdmio-migrations.yml` — are ignored, and a warning naming the file and scope is logged. This stops a foreign
header from fabricating a baseline that would silently skip another assembly's migrations; that assembly runs its
migrations from zero instead.

> One combination cannot be vouched for: an assembly that folded *all* of its migrations into its schema file **and**
> overrides `IDbMigration.Scope` to something other than its assembly's simple name. Its baseline is rejected, and
> its later migrations then fail because the objects already exist. Keep the default scope, or keep at least one
> discovered migration carrying the overridden scope.

If the database already contains migrations, no schema file is applied. That check is global rather than per scope:
schema-first bootstrap works for the first scope to reach a fresh database, or for several assemblies bootstrapped by
a single `DatabaseMigrator`, but a second, separate schema-first migrator against an already-populated database falls
back to running migrations. When `MigrateDatabaseToAsync(targetIdentifier)` is used and any schema's header version
exceeds the target, the bootstrap is skipped entirely, because applying a subset would leave gaps that later
migrations cannot fill.

### Databases Without Scope Information

A database whose migration history has no scopes recorded is brought up to date on the first run: existing rows are
attributed to a scope by matching them against the migrations that were discovered. Rows that no discovered migration
claims stay scope-less, count towards no scope's watermark, and produce a logged warning so you can set their scope
yourself. Existing schema files keep working as they are.

> **One case needs manual repair:** a database that was bootstrapped schema-first from **multiple assemblies** without
> scopes holds a single baseline row (the highest header version across all schemas). Only one scope can be attributed
> from it, so the others have no baseline, and their folded-in migrations get selected again and fail on the objects
> the schema already created. Insert the missing scopes' baseline rows into `mvdmio.migrations` yourself before
> migrating such a database.

## Schema Inspection and Export

```csharp
if (await db.Management.SchemaExistsAsync("billing"))
{
   var exists = await db.Management.TableExistsAsync("billing", "invoices");
}

// Full CREATE script for the current database.
var script = await db.Management.GenerateSchemaScriptAsync(ct);
```

Exported scripts preserve identity columns, `GENERATED ALWAYS AS (…) STORED` columns, and standalone unique indexes.
Their header carries one `-- Migration version: <id> (<name>) [<scope>]` line per scope, so the script can be replayed
as a [schema-first baseline](#embedded-schema-files).

`db.Management.Schema` exposes the catalog reader behind the export for finer-grained inspection:
`GetUserSchemasAsync`, `GetTablesAsync`, `GetIndexesAsync`, `GetConstraintsAsync`, `GetViewsAsync`,
`GetSequencesAsync`, `GetFunctionsAsync`, `GetTriggersAsync`, `GetEnumTypesAsync`, `GetCompositeTypesAsync`,
`GetDomainTypesAsync`, and `GetExtensionsAsync`.

For a file-based schema workflow driven from the command line, use the [CLI tool](#cli-tool)'s `db pull`.

## LISTEN/NOTIFY

Block until a `NOTIFY` arrives on a channel. The wait uses a connection of its own, so it does not tie up the one you
query with:

```csharp
await db.WaitAsync("order_created", ct);

// Or bound the wait — returns false on timeout.
var notified = await db.WaitAsync("order_created", TimeSpan.FromSeconds(30), ct);
```

Synchronous `Wait(channel)` and `Wait(channel, timeout)` overloads are available too.

## Type Handling

These types work out of the box, as parameters and in results, on `db.Dapper` and on [composable
queries](#composable-queries) alike: `DateOnly`, `TimeOnly`, `Uri`, and `Dictionary<string, string>` mapped to
`jsonb`.

Enums are stored as strings, but have to be registered once at startup:

```csharp
services.AddEnumDapperTypeHandlers(typeof(Program).Assembly);
```

Every enum in the given assemblies is mapped. The mapping applies process-wide rather than per service collection, so
calling it once is enough.

### Types the query surface does not know

`db.Dapper` and the query surface keep separate conversion registries, so a Dapper type handler you wrote yourself
does not reach `Query()`. Register the equivalent once at startup, before the first query runs:

```csharp
using LinqToDB;
using mvdmio.Database.PgSQL.Connectors.Linq;

LinqDatabaseConnector.ConfigureMappingSchema(schema =>
{
   schema.SetConverter<Money, decimal>(x => x.Amount);
   schema.SetConverter<decimal, Money>(x => new Money(x));
});
```

Like the Dapper handler registry this applies process-wide. `PGSQL0011` warns at build time when a table model has a
property type the query surface cannot map, so you find out before the query runs.

## Build-Time Diagnostics

The package ships analyzers that catch mistakes at compile time instead of at runtime.

| Code        | Severity | Meaning                                                                           |
|-------------|----------|-----------------------------------------------------------------------------------|
| `PGSQL0001` | Warning  | `IDbMigration` class name does not match `_{YYYYMMDDHHmm}_{Name}`                  |
| `PGSQL0002` | Warning  | `[Table]` class name does not end with `Table`                                     |
| `PGSQL0003` | Error    | `[Table]` class is not `partial`                                                   |
| `PGSQL0004` | Error    | `[Table]` class does not declare exactly one `[PrimaryKey]`                        |
| `PGSQL0005` | Error    | Two properties map to the same column                                              |
| `PGSQL0006` | Error    | Two lookup properties would generate the same method name                          |
| `PGSQL0007` | Error    | No updatable columns, so no update command can be generated                        |
| `PGSQL0008` | Error    | `[Table]` value is not `table` or `schema.table`                                   |
| `PGSQL0009` | Error    | A property is not a public instance property with a public getter and setter        |
| `PGSQL0010` | Error    | A generated type name is already taken by a non-partial type in the same namespace  |
| `PGSQL0011` | Warning  | A property type cannot be mapped by the query surface — register a conversion       |

`PGSQL0001` is a warning rather than an error because you can implement `Identifier` and `Name` yourself instead of
following the naming convention. If you do neither, a misnamed migration class throws the moment those properties are
read. `PGSQL0011` is a warning because the rest of the repository still works — only `Query()` cannot handle that
column until a conversion is registered.

## CLI Tool

If you want a command-line workflow for migrations and schema files, install the companion tool:

```bash
dotnet tool install --global mvdmio.Database.PgSQL.Tool
```

Documentation: [mvdmio.Database.PgSQL.Tool](https://github.com/mvdmio/mvdmio.Database.PgSQL/blob/main/src/mvdmio.Database.PgSQL.Tool/README.md).

## License

MIT. See [LICENSE](https://github.com/mvdmio/mvdmio.Database.PgSQL/blob/main/LICENSE).
