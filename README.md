# mvdmio.Database.PgSQL

PostgreSQL tooling for .NET, in two packages:

- **[`mvdmio.Database.PgSQL`](src/mvdmio.Database.PgSQL/README.md)** — the library you use from application code:
  queries, transactions, bulk operations, generated repositories, migrations, and schema management.
- **[`mvdmio.Database.PgSQL.Tool`](src/mvdmio.Database.PgSQL.Tool/README.md)** — a `dotnet` tool exposed as `db` for
  creating and running migrations, exporting schema files, copying data between environments, and cleaning up
  obsolete migrations.

Both target `net8.0`, `net9.0`, and `net10.0`. Use the library on its own, or add the tool when you also want a
command-line migration workflow.

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

Annotate a table model:

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
var deleted = await repository.DeleteByUserIdAsync(created.UserId, ct);
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
- **Generated repositories**: annotate a table model and get typed CRUD, lookups by primary key and unique column,
  and DI registration generated at build time
- **Migrations from application code**, tracked per scope so several assemblies can migrate one database
  independently, and serialized by an advisory lock so concurrently starting instances apply them exactly once
- **Schema-first bootstrap**: `Schemas/**/*.sql` files are embedded automatically and applied to an empty database
  instead of replaying every migration
- **Schema management**: table and schema existence checks, catalog inspection, and full schema export

Usage and examples: **[library documentation](src/mvdmio.Database.PgSQL/README.md)**.

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
