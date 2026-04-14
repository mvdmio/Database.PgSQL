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

var migrator = new DatabaseMigrator(db, typeof(Program).Assembly);
await migrator.MigrateDatabaseToLatestAsync();
```

### Embedded Schema Files

When a project references `mvdmio.Database.PgSQL` directly, or references another project that does, any `Schemas/**/*.sql` files in that project are automatically included as embedded resources.

This works through the package's `build` and `buildTransitive` MSBuild props files.

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
