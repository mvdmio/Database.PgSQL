# mvdmio.Database.PgSQL

PostgreSQL tooling for .NET, in two packages:

- **[`mvdmio.Database.PgSQL`](src/mvdmio.Database.PgSQL/README.md)** — the library you use from application code:
  queries, transactions, bulk operations, generated repositories, migrations, and schema management.
- **[`mvdmio.Database.PgSQL.Tool`](src/mvdmio.Database.PgSQL.Tool/README.md)** — a `dotnet` tool exposed as `db` for
  creating and running migrations, exporting schema files, copying data between environments, and cleaning up
  obsolete migrations.

Both target `net8.0`, `net9.0`, and `net10.0`. Use the library on its own, or add the tool when you also want a
command-line migration workflow.

The library carries a source generator, so building against it needs the **.NET 8.0.100 SDK or newer** — an older SDK
warns and then skips the generator, leaving the generated repository types undefined.

## Install

```bash
# In your application project
dotnet add package mvdmio.Database.PgSQL

# The CLI, globally...
dotnet tool install --global mvdmio.Database.PgSQL.Tool

# ...or into a tool manifest
dotnet new tool-manifest
dotnet tool install mvdmio.Database.PgSQL.Tool
```

## Query From Code

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

## Or Let The Repository Be Generated For You

Annotate a table definition:

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

...and a typed repository is generated at build time, along with the command and data types it uses:

```csharp
var repository = new UserRepository(db);

var created = await repository.CreateAsync(new CreateUserCommand { UserName = "alice" }, ct);
var found = await repository.GetByUserNameAsync("alice", ct);
var deleted = await repository.DeleteByPrimaryKeyAsync(created.UserId, ct);
```

The primary key may be composite — mark two or more properties `[PrimaryKey]` and the generated lookup, delete and update
address the row by all of them, in the order you declared them.

A property's type also tells the query surface whether its column can hold null, so a predicate over a non-nullable
column reaches PostgreSQL without an `OR column IS NULL` alternative that could never match — and can use an index.
Where the type cannot say it, `[Column(Null = true)]` and `[Column(NotNull = true)]` do.

A column can state how it is stored, too. Enums go in as the text of their member name with nothing to declare;
`[Column(StoredAs = NpgsqlDbType.Integer)]` stores one as a number instead, and `StoredAs = NpgsqlDbType.Jsonb` puts a
`string` into a `jsonb` column. The claim is per column, so two columns of the same enum can differ, and it feeds the
generated commands and `Query()` from one declaration — the two can never disagree about a column.

A column can also be declared the tenant a multi-tenant table is scoped by:

```csharp
[Column(Tenancy = true)]
[PrimaryKey]
public long AccountId { get; set; }
```

Every generated member then constrains that column, so a caller cannot start a query, a lookup, or a write without
supplying the tenant:

| Member | Tenancy column inside the primary key | Tenancy column outside it |
| --- | --- | --- |
| `Query`, `GetAllAsync`, `GetBy{Unique}Async`, `DeleteBy{Unique}Async` | gains a parameter | gains a parameter |
| `GetByPrimaryKeyAsync`, `DeleteByPrimaryKeyAsync` | unchanged | gains a parameter |
| `CreateAsync`, `UpdateAsync` | unchanged — the tenancy column becomes a `required` property on the command instead | unchanged — the tenancy column becomes a `required` property on the command instead |

The guarantee is narrow, and worth stating plainly: it reaches generated code and stops there, nothing checks the
column against the real table, and nothing checks the value you pass against anything. It makes the tenant impossible
to omit. It does not make it impossible to get wrong.

Need a query whose shape is only known at runtime? Every generated repository also hands you an `IQueryable<T>` that
translates to SQL:

```csharp
var page = await repository.Query()
   .Where(x => x.UserName == name)
   .OrderBy(x => x.UserName)
   .Skip(20)
   .Take(20)
   .ToListAsync(ct);
```

Full walkthrough: [Generated Repositories](src/mvdmio.Database.PgSQL/README.md#generated-repositories).

## Migrate From The Command Line

```bash
db init                              # create .mvdmio-migrations.yml
db migration create AddUsersTable    # scaffold a migration
db migrate latest                    # apply pending migrations
db pull                              # export the current schema to Schemas/
```

Refresh a local database from another configured environment:

```bash
db copy --from prod --to local
```

## What The Library Gives You

- **Queries and commands** through Dapper, with `snake_case` to `PascalCase` column mapping and PostgreSQL-typed
  parameters
- **Transactions**, either around a delegate or driven explicitly with an isolation level
- **Bulk operations** built on PostgreSQL binary `COPY`: bulk insert, streaming copy, upsert, insert-or-skip,
  temp-table staging, and table-to-table copy across connections
- **Generated repositories**: annotate a table definition and get typed CRUD, lookups by primary key and unique column,
  and DI registration generated at build time
- **Composable queries**: a deferred `IQueryable<T>` per table for filters, ordering and paging decided at runtime —
  hand it to an OData endpoint or anything else that consumes a queryable, with each column's declared nullability
  carried into the SQL so a predicate stays sargable
- **Table relations**: declare that one table definition points at another and filter, order and eagerly load across it,
  without writing the join
- **Migrations from application code**, tracked per scope so several assemblies can migrate one database
  independently, and serialized by an advisory lock so concurrently starting instances apply them exactly once
- **Schema-first bootstrap**: `Schemas/**/*.sql` files are embedded automatically and applied to an empty database
  instead of replaying every migration
- **Schema management**: table and schema existence checks, catalog inspection, and full schema export

Usage and examples: **[library documentation](src/mvdmio.Database.PgSQL/README.md)**.

Wiring an OData endpoint onto a generated repository's `Query()`:
**[OData walkthrough](test/mvdmio.Database.PgSQL.Tests.Integration.OData/README.md)** — the two settings you must set,
which query options and `$filter` functions reach the database, and where the behaviour differs from an Entity Framework
Core-backed endpoint.

## What The CLI Gives You

| Command                      | Purpose                                                         |
|------------------------------|-----------------------------------------------------------------|
| `db init`                    | Create the `.mvdmio-migrations.yml` configuration file            |
| `db migration create <name>` | Scaffold a timestamped migration class                            |
| `db migrate latest`          | Apply all pending migrations                                      |
| `db migrate to <identifier>` | Apply migrations up to a specific identifier                      |
| `db pull`                    | Export the current database schema to a schema file                |
| `db cleanup`                 | Refresh schema files and delete migrations they have superseded    |
| `db copy --from x --to y`    | Copy all table data between configured environments                |

Every command reads connection strings and project layout from `.mvdmio-migrations.yml`. The commands that talk to a
database — `db migrate latest`, `db migrate to`, and `db pull` — take `--environment`/`-e` to pick which one, or
`--connection-string` to bypass the config.

Options and configuration: **[CLI documentation](src/mvdmio.Database.PgSQL.Tool/README.md)**.

## License

MIT. See [`LICENSE`](LICENSE).
