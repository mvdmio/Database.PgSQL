# mvdmio.Database.PgSQL

PostgreSQL access for .NET applications.

The package combines Dapper and Npgsql with a higher-level API for common PostgreSQL workflows.

## Installation

```bash
dotnet add package mvdmio.Database.PgSQL
```

Targets `net8.0`, `net9.0`, and `net10.0`.

**Requires the .NET 8.0.100 SDK or newer to build.** The package carries the source generator that produces
[generated repositories](#generated-repositories), and an SDK older than that cannot load it: the compiler warns
(`CS9057` or `CS8032`) and then skips the generator without running it, so a `[Table]` class produces nothing and every
name your code expects from it is undefined. If generated types are missing and you see one of those warnings, the SDK is
the reason. Everything else in the package works on any SDK that can target `net8.0`.

## What You Can Do With It

- [Open connections](#connections) directly or through a pooling factory
- [Run SQL queries and commands](#queries-and-commands) through `db.Dapper`
- [Execute work inside transactions](#transactions)
- [Bulk copy, upsert, and insert-or-skip](#bulk-operations) large batches of rows
- [Generate repositories](#generated-repositories) from annotated table definitions
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

Nothing has to be registered for enums: a [generated repository](#generated-repositories) states each enum column's
storage on the column itself, and hand-written SQL reads one back from a `text` column without setup. See
[Type Handling](#type-handling) for what to do when you write one.

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

Annotate a table definition and the package generates a typed repository for it at build time: no runtime reflection, no
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

var all = await repository.GetAllAsync(ct);                              // IEnumerable<UserData>
var byId = await repository.GetByPrimaryKeyAsync(created.UserId, ct);    // UserData?
var byName = await repository.GetByUserNameAsync("alice", ct);           // UserData?

var updated = await repository.UpdateAsync(new UpdateUserCommand
{
   UserId = created.UserId,
   UserName = "alice",
   FirstName = "Alicia",
   LastLoginAt = DateTimeOffset.UtcNow
}, ct);

var deleted = await repository.DeleteByPrimaryKeyAsync(created.UserId, ct);  // false when no row matched
```

A repository takes a `DatabaseConnection` and runs all of its SQL through it, so it joins whatever transaction that
connection has open.

### Composite Primary Keys

Mark two or more properties `[PrimaryKey]` and the key is composite. The order you declare them in is the key order, and
it is contract: it fixes the parameter order of the generated lookup and delete. It says nothing about how a
[relation](#relations) matches columns — a relation states each pair itself, so reordering a key's properties never
changes what a relation resolves.

```csharp
[Table("public.projects")]
public partial class ProjectTable
{
   [PrimaryKey] public long AccountId { get; set; }
   [PrimaryKey] [Generated] public long ProjectId { get; set; }

   public string Name { get; set; } = string.Empty;
}
```

```csharp
var project = await repository.GetByPrimaryKeyAsync(accountId, projectId, ct);
var deleted = await repository.DeleteByPrimaryKeyAsync(accountId, projectId, ct);
```

Everything else is the same as for a single-column key: a data type, a create command, an update command, a repository
and its interface, all generated the same way, with the update addressing the row by every key member. A key member may
be `[Generated]`, so a key that is part supplied by you and part computed by the database works without special
handling. `[Unique]` columns keep their own `GetBy{Property}Async`/`DeleteBy{Property}Async` pair, because the property
name is the only thing telling two unique lookups apart.

A key member may not be nullable — that is a build error, `PGSQL0020`. A nullable key member is a key PostgreSQL would
reject, and it is also what would make the query surface widen a relation's join with an "or both are null" alternative,
which costs the join its index on the second column. Refusing it makes that shape impossible to reach.

### What Gets Generated

For `UserTable`, five types are generated in the same namespace — `public` when the table class is public, `internal`
otherwise:

| Type                | Contains                                                      |
|---------------------|---------------------------------------------------------------|
| `UserData`          | Every mapped property, plus a mirrored property per relation — the type all reads and writes return |
| `CreateUserCommand` | Every property except `[Generated]` ones                       |
| `UpdateUserCommand` | Every primary key property, plus every other non-`[Generated]` property |
| `IUserRepository`   | The repository interface                                       |
| `UserRepository`    | The implementation                                             |

The class name minus its `Table` suffix supplies these names, so `ProductTable` produces `ProductData`,
`CreateProductCommand`, `IProductRepository`, and so on. All five are `partial`, so you can add members to them from
your own files.

On the data type, a `[Generated]` column is mirrored as `{ get; private set; }` — a caller cannot assign a value the
database produces, which is usually the point of marking it. Every other column is `{ get; set; }`, and the command types
keep every column publicly settable including generated ones, because an update addresses its row by a primary key that
may itself be generated. `required` and `init` are not mirrored: these types have no constructor that could satisfy
`required`, and a command has to be assignable to be built.

The repository exposes:

- `CreateAsync(Create…Command, CancellationToken)` → the created row
- `GetAllAsync(CancellationToken)` → every row
- `GetByPrimaryKeyAsync(…, CancellationToken)` → the matching row or `null`; one parameter per primary key property, in
  declaration order. Always this name, whatever the key is called and however many columns it has
- `GetBy{Property}Async(value, CancellationToken)` → the matching row or `null`; one method per `[Unique]` property
- `UpdateAsync(Update…Command, CancellationToken)` → the updated row, matched on every primary key property
- `DeleteByPrimaryKeyAsync(…, CancellationToken)` → `true` when a row was deleted; the same parameters as the lookup
- `DeleteBy{Property}Async(value, CancellationToken)` → `true` when a row was deleted; one method per `[Unique]`
  property
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
ASP.NET Core OData endpoint, where `$filter`, `$orderby`, `$top`, `$skip`, `$count`, `$select`, `$apply` and `$expand`
all reach the database:

```csharp
[HttpGet]
public IActionResult Get(ODataQueryOptions<UserData> options)
{
   var query = options.ApplyTo(
      _repository.Query(),
      new ODataQuerySettings { HandleNullPropagation = HandleNullPropagationOption.False }
   );

   return Ok(((IEnumerable)query).Cast<object>().ToList());
}
```

Two things about that snippet are load-bearing, and neither is discoverable. `HandleNullPropagation` **must** be
disabled — left at its default, OData guards every property access, which breaks `substring` outright and makes every
predicate non-indexable. And the query is materialized inside the action rather than returned as an `IQueryable` for
`[EnableQuery]` to enumerate, because the output formatter enumerates after the response headers are written and turns a
translation failure into a truncated `200` instead of a `400`.

Build the EDM model from the generated `…Data` type, since that is what `Query()` returns, and enable the query options
you want — every one of them is off by default and `$top` is capped at zero, so an unconfigured endpoint rejects
everything:

```csharp
builder.Services.AddControllers().AddOData(
   options => options.Select().Filter().OrderBy().Count().Expand().SetMaxTop(100).AddRouteComponents("odata", model)
);
```

If you allow `$expand`, cap the expansion depth as well — relations are declared one direction at a time, so a model
that declares both directions contains a cycle by construction, and the depth cap is the only thing stopping a client
walking around one until the database gives up.

The full walkthrough — the settings to copy, the functions to block, which query options and `$filter` functions are
known to work, where the behaviour differs from an Entity Framework Core-backed endpoint, and which generated property
types an OData model cannot represent — is in the
[OData walkthrough](https://github.com/mvdmio/mvdmio.Database.PgSQL/blob/main/test/mvdmio.Database.PgSQL.Tests.Integration.OData/README.md).

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

### Relations

A query spans tables along a relation you declared. A relation is declared by a class deriving from
`RelationDefinition<TDeclaring, TTarget>`, naming both table definitions in its type arguments. The relation property
on the table definition is typed as that class — or as a list of it, for a relation to many rows:

```csharp
[Table("public.books")]
public partial class BookTable
{
   [PrimaryKey] [Generated] public long BookId { get; set; }
   public string Title { get; set; } = string.Empty;
   public long? AuthorId { get; set; }

   private AuthorRelation? Author { get; set; }

   private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AuthorId, y => y.AuthorId),
      ];
   }
}

[Table("public.authors")]
public partial class AuthorTable
{
   [PrimaryKey] [Generated] public long AuthorId { get; set; }
   public string Name { get; set; } = string.Empty;

   private BooksRelation? Books { get; set; }

   private class BooksRelation : RelationDefinition<AuthorTable, BookTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AuthorId, y => y.AuthorId),
      ];
   }
}
```

**Upgrading from the attribute form.** Every `[Relation(nameof(...))]` declaration stops compiling — there is one way
to declare a relation now, not two. Converting one is mechanical: fold the property's old target type and the
attribute's foreign-key name(s) into a nested class, and pair each one against the same target member the attribute
used to name positionally. The old and new forms of the pair above:

```csharp
// Before: the property's type named the target, the attribute named the foreign key.
[Relation(nameof(AuthorId))]
public AuthorTable? Author { get; set; }

// After: a class names both tables and states the pair itself.
private AuthorRelation? Author { get; set; }

private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
{
   public override IReadOnlyList<RelationKey> Keys => [
      Key(x => x.AuthorId, y => y.AuthorId),
   ];
}
```

A relation property no longer has to be `public` — only that it has a getter and a setter — because a nested
definition class more naturally stays `private`, and a `public` property cannot be typed as a less accessible nested
class (`CS0053`). Keep the property and its nested definition class at matching accessibility.

**Stating the pairs.** `Keys` states one `Key(declaringProperty, targetProperty)` pair per joined column, built from a
`protected static` helper on the base class. Each side is a direct property reference — `x => x.Column` — so a rename
is a build error rather than a silently wrong join, and a pair whose two sides hold different types does not compile.
`Key` has two overloads: matching types on both sides, and a nullable left side against a non-nullable right — the
ordinary outer-join shape, where a foreign key may hold null but the key it targets never does. The order the pairs
are listed in carries no meaning; they are combined with `&&`, so reordering them changes nothing. A composite key
needs no different shape from a single-column one — one `Key(…)` per column, in any order:

```csharp
[Table("public.tasks")]
public partial class TaskTable
{
   [PrimaryKey] public long AccountId { get; set; }
   [PrimaryKey] public long TaskId { get; set; }
   public long ProjectId { get; set; }

   private ProjectRelation? Project { get; set; }

   private class ProjectRelation : RelationDefinition<TaskTable, ProjectTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AccountId, y => y.AccountId),
         Key(x => x.ProjectId, y => y.ProjectId),
      ];
   }
}
```

A foreign key does not have to be part of the declaring table's own key, and it is fine for it to overlap: a tenancy
column appearing on both sides of the join is the ordinary case. Stating no pairs at all is a build error, `PGSQL0029`
— it would register a cross join, and there is no sensible default. Either side of a pair not being a direct property
reference on its own table is `PGSQL0030`.

**The cardinality and uniqueness claim.** Typed as the definition class itself, a relation reaches one row, and the
property must be nullable — a relation is always an outer join. Typed as a collection of it — `List<T>`, `IList<T>`,
`ICollection<T>`, `IEnumerable<T>`, `IReadOnlyList<T>` and `IReadOnlyCollection<T>` are accepted — it reaches many
rows; the generated data type always mirrors it as a `List<T>` initialized to empty. A relation to one row is a claim
that its pairs reach at most one target row, exactly like every other claim a table definition makes: the target-side
columns must contain something the target claims unique — its primary key, or a `[Unique]` column; a superset of a
unique set still counts. Pairing against nothing the target claims unique is a build *warning*, `PGSQL0031`, not an
error — a relation whose condition happens to make the pairing unique still builds, because the check reads the pairs
and cannot see the condition.

Each direction is declared on its own: a relation to a parent does not oblige the parent to declare the collection
back. Two relations may point at the same target — a `CreatedByUserId` and an `UpdatedByUserId` both reaching the user
table — and a relation may target its own table, in either direction, which is how a hierarchy works. Many-to-many
needs no new concept: declare a relation to the join table, which is a table definition like any other, and a relation
from there to the far side.

**The condition.** A definition class may override `Condition` — an ordinary `Expression<Func<TDeclaring, TTarget,
bool>>` over the two rows, checked by the compiler exactly where it is written. It narrows both filtering and
materializing, because it belongs to the relation itself rather than to any one query. This is what lets two relations
pair the *same* columns and still reach different rows — the shape a table holding a kind column beside an identifier
needs, without a real column per kind:

```csharp
public enum LinkTargetKind { Person, Asset }

[Table("public.links")]
public partial class LinkTable
{
   [PrimaryKey] [Generated] public long LinkId { get; set; }
   public LinkTargetKind Kind { get; set; }
   public long TargetId { get; set; }

   private PersonRelation? Person { get; set; }
   private AssetRelation? Asset { get; set; }

   private class PersonRelation : RelationDefinition<LinkTable, PersonTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.TargetId, y => y.PersonId),
      ];

      public override Expression<Func<LinkTable, PersonTable, bool>> Condition
         => (link, person) => link.Kind == LinkTargetKind.Person;
   }

   private class AssetRelation : RelationDefinition<LinkTable, AssetTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.TargetId, y => y.AssetId),
      ];

      public override Expression<Func<LinkTable, AssetTable, bool>> Condition
         => (link, asset) => link.Kind == LinkTargetKind.Asset;
   }
}
```

`Person` and `Asset` both pair the same `TargetId` column, and each resolves only the rows its own condition matches
— reaching through one never returns the other kind's row. Omitting `Condition` costs nothing extra: it defaults to
no condition, so an ordinary relation is unaffected. A condition may reach through another relation property — it is
a member like any other — but it is policed only at the two parameters themselves: a member touched directly on
either one must exist on that table's generated data type, or the build fails on your own line (`PGSQL0032`), rather
than on a compile error inside generated source you never wrote. Anything beyond a direct member access — a method
call the query surface may not translate, for instance — passes through unchecked, because refusing it here would
reject expressions the library has no test for.

If one relation on a table pairs the same columns as another that carries a condition, but states no condition of its
own, that is a build warning, `PGSQL0034` — it may silently resolve every kind the conditioned ones distinguish
between, which usually means a condition was forgotten rather than genuinely meant to be absent.

Both directions of a polymorphic relation are declared the same way: `PersonTable` and `AssetTable` each get their own
relation back to `LinkTable`, with the matching condition, exactly as any other reverse relation is declared.

`[Relation]` may still be written on a relation property, purely to spell the intent out — it changes nothing and is
never required. Writing it on a property whose type is not a relation definition (or a supported collection of one)
is a build error, `PGSQL0033`, so the attribute can never say something untrue.

Everything else costs nothing extra. Filtering, ordering, existence predicates and `Include` all read exactly as they
did before, and the generated join constrains every key column plus the condition, joined with `&&`, so the database
can use a composite index on the columns.

A relation is not a column. It gets no column mapping, no `GetBy…`/`DeleteBy…` pair, and no place in the create and
update commands, which stay as flat as the table they write to. It changes no SQL on `db.Dapper`, and **it emits no
DDL**: a relation definition is a claim about columns that already exist, and nothing checks it against the real
schema — pair against the wrong property and you get a wrong join at runtime, exactly as with a wrong `[Column]` name.
If you want the database itself to refuse a link pointing at a row that does not exist, you still write the generated
column and its foreign key constraint in a migration by hand; this feature is about traversal in C#, not about what
exists in PostgreSQL.

#### Filtering and ordering across a relation

Nothing new to learn: reach through the relation property and the join is generated for you.

```csharp
var byTolkien = await bookRepository.Query()
   .Where(x => x.Author!.Name == "Tolkien")
   .OrderBy(x => x.Author!.Name)
   .ToListAsync(ct);

// Two hops, and back the other way, work the same.
var mentoredByTolkien = await bookRepository.Query().Where(x => x.Author!.Mentor!.Name == "Tolkien").ToListAsync(ct);
var authorsOfNarnia = await authorRepository.Query().Where(x => x.Books.Any(b => b.Title == "Narnia")).ToListAsync(ct);
```

A relation is an outer join, so a book whose `AuthorId` is null is still returned by a query that does not mention the
author. Once a predicate lands on the far side it narrows the result as the C# reads — `x.Author!.Name == "Tolkien"`
drops that book, and so does `x.Author!.Name != null`.

#### Materializing the related rows

Filtering across a relation does not fetch it. Ask for the rows with `Include`, and chain further levels with
`ThenInclude`:

```csharp
var authors = await authorRepository.Query()
   .Include(x => x.Books)
      .ThenInclude(x => x.Editor)
   .ToListAsync(ct);
```

Without an `Include`, a relation property stays `null` — or empty, for a collection. Nothing is fetched behind your
back and no query is ever issued from a property getter, so "not loaded" never turns into a surprise round trip.

`Include` costs differently in each direction, and it is worth knowing which you are paying for:

| Relation | Cost |
|----------|------|
| To one row  | Folds into the query as an outer join. No extra statement |
| To many rows | One **additional statement per level**, each of which re-runs the query above it as a derived table rather than passing it a list of keys |

So a three-level include over collections is four statements, each re-deriving its ancestors. This is the query
provider's strategy and it cannot be configured.

Because each detail statement re-derives its parents that way, a filter on the main query decides **which parents get
detail rows** — it does not narrow the detail rows themselves. On a self-referencing hierarchy that is worth saying
out loud: filtering the main query down to the roots still loads every child, including the rows the filter excluded.
Scoping the detail rows takes the filtered overload:

```csharp
var authors = await authorRepository.Query()
   .Include(x => x.Books, books => books.Where(b => b.PublishedAt > cutoff))
   .ToListAsync(ct);
```

> **If you are exposing a relation through an OData endpoint, read this before you ship.** A `$expand` does not go
> through `Include` at all — the front-end composes the expansion itself — but it has a failure mode of its own, and it
> is silent. With the ASP.NET Core OData defaults left alone, a `$expand` over a relation to
> **many** rows comes back as an **empty collection and without any error** — for every parent, including the ones that
> do have related rows. The cause is the null-propagation rewriting OData applies to query providers it does not
> recognise, and it recognises them by namespace, from a list this package's provider is not on. Set
> `HandleNullPropagation = HandleNullPropagationOption.False` on the query settings and an
> expansion returns its rows. Nothing inside this package can detect the situation: the query surface composes and sends
> exactly the same statement either way, so there is no failure to catch. Expanding a relation to **one** row is
> unaffected — it folds into that statement as a join and survives the rewriting intact.
>
> Each symptom above — the empty collection, the identical statement, the to-one direction surviving — is pinned by a
> test in the OData walkthrough linked earlier. How many statements the endpoint issues *beyond* that one is not
> something those tests can count, so nothing here claims it.

Operators may sit between `Include` and `ThenInclude`; the two only have to be named as one chain, which means naming
the intermediate result as `IIncludedQueryable<TEntity, TProperty>` again:

```csharp
var filtered = (IIncludedQueryable<BookData, AuthorData>)bookRepository.Query()
   .Include(x => x.Author)
   .Where(x => x.Title == "Narnia");

var books = await filtered.ThenInclude(x => x.Mentor).ToListAsync(ct);
```

`Include` and `ThenInclude` live in `mvdmio.Database.PgSQL` alongside the awaiting operators, so the same one `using`
covers them. They need a query that came from a generated repository's `Query()`; handed anything else — an in-memory
fake, for instance — they throw `NotSupportedException` rather than quietly loading nothing.

### Attributes

| Attribute       | Effect                                                                                                |
|-----------------|-------------------------------------------------------------------------------------------------------|
| `[Table("…")]`  | Marks the class and names the table. Takes `schema.table`, or `table` for the `public` schema           |
| `[PrimaryKey]`  | Marks a property as part of the key used by `UpdateAsync`, `GetByPrimaryKeyAsync` and `DeleteByPrimaryKeyAsync`. At least one property must carry it; two or more makes the key composite, in declaration order |
| `[Unique]`      | Adds a `GetBy…`/`DeleteBy…` pair for that property                                                     |
| `[Column("…")]` | States facts about the column: its name, whether it can hold null (`Null`, `NotNull`), how it is stored (`StoredAs`), and whether it is a tenancy column (`Tenancy`). Without a name, the property name is converted to `snake_case` |
| `[Generated]`   | The database produces the value (identity, computed, or defaulted): it is read back but never written   |
| `[Relation]` | Optional: spells out that a property typed as a `RelationDefinition<,>` (or a collection of one) is a relation. Writing it on any other property fails the build |

The `snake_case` conversion inserts an underscore before every uppercase letter, so `UserId` becomes `user_id` but
`UserID` becomes `user_i_d` — name the column explicitly with `[Column]` when the property contains an acronym.

### Column Nullability

A column is nullable unless the definition says otherwise, and for almost every property the type already says it:

| Property                                     | Column      |
|----------------------------------------------|-------------|
| `long`, `DateOnly`, any non-nullable value type | not null |
| `long?`, any `Nullable<T>`                   | nullable    |
| `string` in a file with nullable reference types on | not null |
| `string?`                                    | nullable    |
| `string` in a nullable-oblivious file        | nullable    |
| Any `[PrimaryKey]` property                  | not null    |
| `[Column(Null = true)]`                      | nullable    |
| `[Column(NotNull = true)]`                   | not null    |

This is worth stating correctly rather than leaving to the default. `Query()` compares like C# does — an inequality
matches the rows where the column is null, per the OData specification too — so on a nullable column it renders an
`OR column IS NULL` alternative. On a column that cannot hold null that alternative can never match, and it costs the
predicate its index: measured on a two-column join over 50k-row tables, 232x runtime on a nested loop.

`[Column]`'s two properties are for the cases the type cannot express:

```csharp
// The column permits null, but the property type cannot say so.
[Column(Null = true)]
public string Nickname { get; set; } = string.Empty;

// A nullable-oblivious file, where the annotation that would say this cannot be written.
[Column("first_name", NotNull = true)]
public string FirstName { get; set; }
```

Neither `[Unique]` nor `[Generated]` implies anything about nullability: PostgreSQL permits any number of nulls in a
unique index, and a database-supplied value can be null — a stored generated column over a polymorphic discriminator is
null for every kind but its own.

A claim that contradicts itself is a build error, `PGSQL0021` — `NotNull` on a `string?`, `Null` on a `long`, both at
once, or `Null` on a `[PrimaryKey]` property. It does not stop the table generating: the claim is dropped and the column
keeps whatever its type and key membership already settle.

Nothing verifies a claim against the real table, the same way nothing verifies a column name. A column claimed not-null
that does hold null is not caught when the row is read — the null arrives in a property typed non-nullable. What it costs
is rows: an inequality over that column omits the ones where it is null. Claim not-null because the table says
`NOT NULL`, not because the value is usually present.

### Column Storage

For most properties the type settles how the value reaches PostgreSQL and there is nothing to say. Where it does not,
`[Column(StoredAs = …)]` states it, spelled with Npgsql's own `NpgsqlDbType`:

```csharp
using NpgsqlTypes;

// Stored as the text of its member name — 'Open', 'Closed'. This is the default; the attribute is not needed.
public TaskState State { get; set; }

// The same enum on another table, stored as its underlying number.
[Column(StoredAs = NpgsqlDbType.Integer)]
public TaskState Priority { get; set; }

// Arbitrary JSON held as a string. PostgreSQL will not cast text to jsonb implicitly, so this has to be stated.
[Column(StoredAs = NpgsqlDbType.Jsonb)]
public required string Document { get; init; }
```

The claim is per column, not per type, so two columns of one enum can be stored differently. It is read once and applied
in two places at once — the parameter the generated command binds, and the mapping `Query()` translates against — so the
two cannot disagree about a column. **An enum is stored as text unless you say otherwise**, which is what this package
has always documented; if you were relying on `Query()` sending the underlying number, claim it explicitly.

Nothing verifies the claim against the real table, the same way nothing verifies a column name.

#### What the package tests

A claim outside this table is **permitted** — the library carries it and the driver decides — but only these are
exercised, so only these are known to round-trip:

| Property type                | Without a claim               | Claims that are tested                     |
|------------------------------|-------------------------------|--------------------------------------------|
| `enum`, `enum?`              | the member name, as text      | `Text`, `Smallint`, `Integer`, `Bigint`     |
| `string`, `string?`          | text, with no cast emitted    | `Text`, `Json`, `Jsonb`                     |
| `Dictionary<string, string>` | `jsonb`                       | `Jsonb`, `Json`                             |
| `sbyte`, `sbyte?`            | `smallint`, widened for you   | `Smallint`                                  |
| anything else                | whatever the driver infers    | —                                           |

Reading an enum is case-insensitive on both surfaces, so a stored value differing in case from the member name still
reads. Two enum members differing only in case will throw when either surface parses them, and nothing diagnoses that.

Three refusals, all at build time:

- **`PGSQL0022`** — the claim cannot be honoured for the property's type. Only combinations with a test demonstrating the
  failure are refused, so this list is short by design: an integral claim on a `string` is the one it starts with. The
  claim is dropped and the column is bound as an unclaimed one would be.
- **`PGSQL0023`** — `ushort`, `uint` and `ulong` cannot be column types. Npgsql has no integer or numeric mapping for any
  of them, so a repository over one would read and filter and then throw on every insert. Use `int`, `long` or `decimal`.
- **`PGSQL0024`** — a warning: the claim has no equivalent on the query surface, so generated commands use it and
  `Query()` does not. The network-address and geometry types are the ones this covers.

Serializing an arbitrary object to `jsonb` is not supported: only `string` and `Dictionary<string, string>` are. Native
PostgreSQL enum types are not supported either — a claim describes how a value is bound, not a type the driver has to be
taught.

### Tenancy Column

On a multi-tenant table, `[Column(Tenancy = true)]` names the column every generated member must constrain, so leaving
the tenant out is a build error instead of a review responsibility:

```csharp
[Table("public.projects")]
public partial class ProjectTable
{
   [Column(Tenancy = true)]
   [PrimaryKey] public long AccountId { get; set; }
   [PrimaryKey] [Generated] public long ProjectId { get; set; }

   [Unique] public string Name { get; set; } = string.Empty;
}
```

Every generated member constrains the column. A member that already constrains it — because it is part of the primary
key the member addresses a row by, or because it *is* the unique column being looked up — gains nothing; every other
member takes the value as a parameter, tenancy parameters first and in declaration order, ahead of key parameters and
ahead of a unique column's value:

| Member | Tenancy column inside the primary key | Tenancy column outside it |
| --- | --- | --- |
| `Query` | gains a parameter | gains a parameter |
| `GetAllAsync` | gains a parameter | gains a parameter |
| `GetBy{Unique}Async` | gains a parameter * | gains a parameter * |
| `DeleteBy{Unique}Async` | gains a parameter * | gains a parameter * |
| `GetByPrimaryKeyAsync` | unchanged | gains a parameter |
| `DeleteByPrimaryKeyAsync` | unchanged | gains a parameter |
| `CreateAsync` | unchanged | unchanged |
| `UpdateAsync` | unchanged | unchanged |

\* Except for the tenancy column's own lookup and delete. Those already constrain it, so they take that value once.

`Query()`'s optional `commandTimeout` stays last, and the predicate it applies is already part of the queryable it
returns — a query front-end composing on top of it cannot remove it.

`CreateAsync` and `UpdateAsync` keep their single command parameter. The tenancy column becomes a `required` property
on both generated command types instead, so a construction site that omits it fails to build. It is not added to the
generated data type, which Dapper materializes through a parameterless constructor and so cannot satisfy `required`.

Where the tenancy column sits outside the primary key, a generated update also moves it from the `SET` list to the
`WHERE` clause: the update addresses its row by every key member plus every tenancy column not already among them, and
assigns every other updatable column. An update aimed at another tenant's row then matches nothing and throws, exactly
as one against a missing key already does. A table whose only assignable column was the tenancy column has nothing
left to assign once it moves to the `WHERE` clause, which is the same build error, `PGSQL0007`, a table with no
updatable columns already produces — it will just read as unrelated to the tenancy declaration that caused it.

More than one column may carry `Tenancy = true`. Each is constrained independently, in declaration order, which is the
shape a two-level tenant — an account and a workspace, say — needs.

Everywhere else the column is ordinary: it keeps its nullability claim, its storage claim, its place in the select and
returning lists, and its property on the generated data type.

The guarantee is narrow, and worth stating plainly. It reaches generated code and stops there — `db.Linq.Query<TData>()`
is still reachable directly, and hand-written Dapper is untouched. Nothing checks the column against the real table,
and nothing checks the value passed against anything: the library holds no tenant of its own and cannot tell a right
value from a wrong one. It makes the tenant impossible to omit. It does not make it impossible to get wrong.

Two diagnostics abandon the table when the declaration cannot be honoured, the way a malformed key does:

- **`PGSQL0025`** — the tenancy column is nullable. A null tenant matches no row, so every generated member would
  return nothing.
- **`PGSQL0026`** — the tenancy column is `[Generated]`. Such a column is on no command type, so there is no property
  to make `required`.

A third warns rather than refusing anything:

- **`PGSQL0027`** — a relation could reach across tenants. Checked pair by pair and direction-free: a tenancy column on
  either table's side of a joined pair must be paired with a tenancy column on the other side, and a tenancy column
  that sits outside every pair entirely is paired with nothing, which is the same failure. This covers both directions
  — a relation whose target is wholly untenanted still warns for the *declaring* table's own unpinned tenancy column.
  It drops nothing: the relation still generates, one warning per unpaired tenancy column, naming the relation
  property. A conditioned relation pairing the tenancy column on both sides reports nothing, and so does a relation
  whose target's whole primary key is the tenancy column, reached by that one pair plus a condition — the per-tenant
  singleton shape.

### Requirements

The generator reports a build error when a table definition does not satisfy these:

- The class is `partial`
- `[Table]` names a valid `table` or `schema.table`
- At least one property is marked `[PrimaryKey]`, and no property carrying it is nullable
- At least one property is neither part of the primary key nor `[Generated]`, so there is something to update
- Mapped properties are public, non-static, not indexers, and have a public getter and a setter. The setter's
  accessibility does not matter — `{ get; private set; }`, `{ get; init; }` and `{ get; protected set; }` are all legal
  columns, because a table definition is purely declarative and is never instantiated. A setter has to be there, though:
  a get-only or expression-bodied member is a computed value rather than a column
- No mapped property is typed `ushort`, `uint` or `ulong`, which no PostgreSQL type accepts
- No `[Column]` states a storage claim that cannot be honoured for the property's type
- No two properties map to the same column, no two `[Unique]` properties yield the same method name, and no `[Unique]`
  property is named so that its lookup would collide with `GetByPrimaryKeyAsync`
- A relation property is a class deriving `RelationDefinition<,>` or a supported collection of one, is nullable when it
  points at one row, carries no `[Column]`, states at least one key pair, and each pair is a direct property reference
  on both sides. The definition class's `TDeclaring` type argument must be the table definition the property is
  declared on, and its `TTarget` must be a `[Table]` class in the same compilation

A tenancy column's `required` property lands in the consumer's own compilation, not this package's, so a project
declaring one needs **C# 11 or newer**. Every framework this package targets defaults above that, but a consumer
pinning `LangVersion` lower will not compile.

A separate analyzer warns — rather than errors — when the class name does not end with `Table`, since the suffix only
determines the generated names. See [Build-Time Diagnostics](#build-time-diagnostics) for the codes.

### Dependency Injection

Every assembly containing table definitions also gets an `IServiceCollection` extension named after it — assembly
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

Enums need nothing on a [generated repository](#generated-repositories): each enum column states how it is stored and the
default is text — see [Column Storage](#column-storage).

For hand-written SQL, an enum is read back from a `text` column without any setup, case-insensitively. Writing one is the
part to know about: Dapper resolves an enum parameter to its **underlying number** before any type handler is consulted,
so bind the member name yourself when the column is text.

```csharp
// Sends 'Closed'.
["state"] = state.ToString()

// Sends 2.
["state"] = state
```

`services.AddEnumDapperTypeHandlers(typeof(Program).Assembly)` registers a handler for every enum in the given
assemblies, process-wide. It affects neither of the two lines above — Dapper never reaches it for a parameter — and it
does not reach a generated repository either, whose binding is settled by the column's own storage claim.

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

Like the Dapper handler registry this applies process-wide. `PGSQL0011` warns at build time when a table definition has a
property type the query surface cannot map, so you find out before the query runs.

## Build-Time Diagnostics

The package ships analyzers that catch mistakes at compile time instead of at runtime.

| Code        | Severity | Meaning                                                                           |
|-------------|----------|-----------------------------------------------------------------------------------|
| `PGSQL0001` | Warning  | `IDbMigration` class name does not match `_{YYYYMMDDHHmm}_{Name}`                  |
| `PGSQL0002` | Warning  | `[Table]` class name does not end with `Table`                                     |
| `PGSQL0003` | Error    | `[Table]` class is not `partial`                                                   |
| `PGSQL0004` | Error    | `[Table]` class declares no `[PrimaryKey]` property                                |
| `PGSQL0005` | Error    | Two properties map to the same column                                              |
| `PGSQL0006` | Error    | Two `[Unique]` properties would generate the same method name                       |
| `PGSQL0007` | Error    | No updatable columns, so no update command can be generated                        |
| `PGSQL0008` | Error    | `[Table]` value is not `table` or `schema.table`                                   |
| `PGSQL0009` | Error    | A property is not a public instance property with a public getter and a setter of any accessibility |
| `PGSQL0010` | Error    | A generated name is already taken — by a non-partial type in the same namespace, or by the primary key's own lookup |
| `PGSQL0011` | Warning  | A property type cannot be mapped by the query surface — register a conversion       |
| `PGSQL0014` | Error    | A relation's target is not a `[Table]` class in the same compilation                |
| `PGSQL0015` | Error    | A relation to one row is not nullable                                                |
| `PGSQL0016` | Error    | A relation property's type is neither a relation definition nor a supported collection of one |
| `PGSQL0017` | Error    | A relation property is not an instance property with a getter and a setter, or is an indexer |
| `PGSQL0018` | Error    | A property carries both `[Relation]` and `[Column]`                                  |
| `PGSQL0020` | Error    | A `[PrimaryKey]` property is nullable                                                |
| `PGSQL0021` | Error    | A `[Column]` states a nullability that contradicts the property's type, its key membership, or itself |
| `PGSQL0022` | Error    | A `[Column]` states a `StoredAs` that cannot be honoured for the property's type      |
| `PGSQL0023` | Error    | A property is typed `ushort`, `uint` or `ulong`, which no PostgreSQL type accepts     |
| `PGSQL0024` | Warning  | A `[Column]`'s `StoredAs` has no query surface equivalent — commands use it, `Query()` does not |
| `PGSQL0025` | Error    | A tenancy column is nullable                                                          |
| `PGSQL0026` | Error    | A tenancy column is `[Generated]`                                                     |
| `PGSQL0027` | Warning  | A relation could reach across tenants                                                |
| `PGSQL0028` | Error    | A relation definition's `TDeclaring` type argument is not the table the property is declared on |
| `PGSQL0029` | Error    | A relation's `Keys` override states no pairs                                          |
| `PGSQL0030` | Error    | Either side of a relation key pair is not a direct property reference                 |
| `PGSQL0031` | Warning  | A relation to one row pairs against nothing the target claims unique                  |
| `PGSQL0032` | Error    | A relation condition touches a member with no counterpart on the generated data type   |
| `PGSQL0033` | Error    | `[Relation]` sits on a property whose type is not a relation definition               |
| `PGSQL0034` | Warning  | A relation pairs the same columns as another that carries a condition, but states none itself |
| `PGSQL0035` | Error    | A relation pairs against a target column that is `[Unique]` but nullable              |

`PGSQL0001` is a warning rather than an error because you can implement `Identifier` and `Name` yourself instead of
following the naming convention. If you do neither, a misnamed migration class throws the moment those properties are
read. `PGSQL0011` is a warning because the rest of the repository still works — only `Query()` cannot handle that
column until a conversion is registered. `PGSQL0024` is a warning for the same reason, one surface down: commands honour
the claim and only `Query()` cannot. `PGSQL0027`, `PGSQL0031` and `PGSQL0034` are warnings too, and drop nothing at
all — the relation they name still generates; each flags something worth a second look rather than a malformed
declaration. `PGSQL0014` through `PGSQL0018` and `PGSQL0028` through `PGSQL0035` drop only
the relation they describe and let the rest of the table generate, so the message you read is the mistake you made
rather than a wall of type-not-found errors from everything that names the missing data type. `PGSQL0021` through
`PGSQL0024` abandon nothing at all either: a contradictory nullability claim, a refused storage claim and an
unwritable property type all leave every generated signature well-defined. Everything else — including `PGSQL0020`,
`PGSQL0025` and `PGSQL0026` — abandons the table, because a malformed key or a malformed tenancy declaration leaves
every generated signature undefined rather than one relation.

`PGSQL0012`, `PGSQL0013` and `PGSQL0019` — the old attribute-argument form's foreign-key-name and arity checks — are
retired along with that form. Their ids are not reused.

`PGSQL0023` exists rather than being folded into `PGSQL0011` because that warning's advice — register a conversion —
cannot help: there is no PostgreSQL type to convert to.

## CLI Tool

If you want a command-line workflow for migrations and schema files, install the companion tool:

```bash
dotnet tool install --global mvdmio.Database.PgSQL.Tool
```

Documentation: [mvdmio.Database.PgSQL.Tool](https://github.com/mvdmio/mvdmio.Database.PgSQL/blob/main/src/mvdmio.Database.PgSQL.Tool/README.md).

## License

MIT. See [LICENSE](https://github.com/mvdmio/mvdmio.Database.PgSQL/blob/main/LICENSE).
