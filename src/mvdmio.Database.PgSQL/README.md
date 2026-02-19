# mvdmio.Database.PgSQL

A .NET library that wraps [Dapper](https://github.com/DapperLib/Dapper) and [Npgsql](https://www.npgsql.org/) to simplify PostgreSQL database interactions. Provides managed connections, transaction handling, bulk operations via PostgreSQL's binary COPY protocol, and a migration framework.

Targets .NET 8.0, .NET 9.0, and .NET 10.0.

## Installation

```bash
# Main library
dotnet add package mvdmio.Database.PgSQL

# CLI tool for migrations (optional)
dotnet tool install --global mvdmio.Database.PgSQL.Tool
```

## Quick Start

```csharp
using mvdmio.Database.PgSQL;

// Create a connection
await using var db = new DatabaseConnection("Host=localhost;Database=mydb;Username=postgres;Password=secret");

// Execute a query
var users = await db.Dapper.QueryAsync<User>(
    "SELECT * FROM users WHERE active = :active",
    new Dictionary<string, object?> { ["active"] = true }
);

// Execute a command
await db.Dapper.ExecuteAsync(
    "INSERT INTO users (name, email) VALUES (:name, :email)",
    new Dictionary<string, object?> { ["name"] = "Alice", ["email"] = "alice@example.com" }
);
```

## Core Concepts

### DatabaseConnection

`DatabaseConnection` is the main entry point. It manages the underlying Npgsql connection and exposes three connectors:

| Connector | Purpose |
|-----------|---------|
| `db.Dapper` | SQL queries and commands via Dapper |
| `db.Bulk` | High-performance bulk operations (COPY, upsert) |
| `db.Management` | Database management (schema/table existence checks) |

Connections are managed automatically. Each operation opens a connection if needed and closes it afterward. If a connection is already open (e.g., within a transaction), it is reused.

```csharp
// From a connection string (creates and owns the NpgsqlDataSource)
await using var db = new DatabaseConnection("Host=localhost;Database=mydb;...");

// From an existing NpgsqlDataSource (does not take ownership)
await using var db = new DatabaseConnection(existingDataSource);
```

### DatabaseConnectionFactory

For applications that create multiple connections to the same database, use `DatabaseConnectionFactory`. It caches `NpgsqlDataSource` instances per connection string, avoiding the overhead of creating a new data source for each connection. It also auto-registers all built-in Dapper type handlers.

```csharp
using mvdmio.Database.PgSQL;

await using var factory = new DatabaseConnectionFactory();

// Each call reuses the same underlying NpgsqlDataSource
await using var db1 = factory.ForConnectionString("Host=localhost;Database=mydb;...");
await using var db2 = factory.ForConnectionString("Host=localhost;Database=mydb;...");

// Optionally configure the NpgsqlDataSourceBuilder
await using var db3 = factory.ForConnectionString("Host=localhost;Database=mydb;...", builder =>
{
    builder.UseJsonNet();
});
```

### Dependency Injection

Register enum type handlers for all enums in your assemblies:

```csharp
services.AddEnumDapperTypeHandlers(typeof(MyEnum).Assembly);
```

This scans the given assemblies for all enum types and registers them as string-based type handlers so enums are stored as text in PostgreSQL.

## Querying with Dapper

All Dapper methods are available through `db.Dapper`. Parameters are passed as `IDictionary<string, object?>` and referenced in SQL with the `:paramName` syntax.

### Basic Queries

```csharp
// Query multiple rows
var users = await db.Dapper.QueryAsync<User>(
    "SELECT * FROM users WHERE department = :dept",
    new Dictionary<string, object?> { ["dept"] = "Engineering" }
);

// Query a single row
var user = await db.Dapper.QuerySingleOrDefaultAsync<User>(
    "SELECT * FROM users WHERE id = :id",
    new Dictionary<string, object?> { ["id"] = 42 }
);

// Query the first row
var latest = await db.Dapper.QueryFirstAsync<User>(
    "SELECT * FROM users ORDER BY created_at DESC LIMIT 1"
);

// Execute a scalar query
var count = await db.Dapper.ExecuteScalarAsync<int>(
    "SELECT COUNT(*) FROM users WHERE active = :active",
    new Dictionary<string, object?> { ["active"] = true }
);
```

### Multi-Mapping

Map a single query to multiple types (e.g., for joins):

```csharp
var usersWithCompany = await db.Dapper.QueryAsync<User, Company, UserWithCompany>(
    """
    SELECT u.*, c.*
    FROM users u
    JOIN companies c ON u.company_id = c.id
    WHERE c.name = :companyName
    """,
    splitOn: "id",
    map: (user, company) => new UserWithCompany { User = user, Company = company },
    parameters: new Dictionary<string, object?> { ["companyName"] = "Acme" }
);
```

Multi-mapping supports up to 6 types.

### Multiple Result Sets

```csharp
var (users, companies) = await db.Dapper.QueryMultipleAsync<(IEnumerable<User>, IEnumerable<Company>)>(
    "SELECT * FROM users; SELECT * FROM companies;",
    reader => (reader.Read<User>(), reader.Read<Company>())
);
```

### Command Timeout

All methods accept an optional `commandTimeout` parameter:

```csharp
var result = await db.Dapper.QueryAsync<Report>(
    "SELECT * FROM generate_large_report()",
    commandTimeout: TimeSpan.FromMinutes(5)
);
```

### Column Name Mapping

The library automatically maps `snake_case` database columns to `PascalCase` C# properties. A column named `first_name` maps to a property named `FirstName` without any configuration.

## Transactions

### Automatic Transaction Management

The simplest way to use transactions is with `InTransactionAsync`. It commits on success and rolls back on exception:

```csharp
await db.InTransactionAsync(async () =>
{
    await db.Dapper.ExecuteAsync("INSERT INTO orders (user_id, total) VALUES (:userId, :total)",
        new Dictionary<string, object?> { ["userId"] = 1, ["total"] = 99.99 });

    await db.Dapper.ExecuteAsync("UPDATE inventory SET quantity = quantity - 1 WHERE product_id = :productId",
        new Dictionary<string, object?> { ["productId"] = 42 });
});

// With a return value
var orderId = await db.InTransactionAsync(async () =>
{
    return await db.Dapper.ExecuteScalarAsync<long>(
        "INSERT INTO orders (user_id, total) VALUES (:userId, :total) RETURNING id",
        new Dictionary<string, object?> { ["userId"] = 1, ["total"] = 99.99 }
    );
});
```

### Manual Transaction Management

For more control, manage transactions explicitly:

```csharp
await db.BeginTransactionAsync();

try
{
    await db.Dapper.ExecuteAsync("INSERT INTO ...", parameters);
    await db.Dapper.ExecuteAsync("UPDATE ...", parameters);
    await db.CommitTransactionAsync();
}
catch
{
    await db.RollbackTransactionAsync();
    throw;
}
```

Transactions support safe nesting. Calling `BeginTransactionAsync` when a transaction is already active returns `false` and reuses the existing transaction. Only the outermost transaction controls commit/rollback.

## Bulk Operations

The `db.Bulk` connector provides high-performance operations using PostgreSQL's binary COPY protocol. Column mappings define how C# objects are converted to database values.

### Column Mappings

All bulk operations require a column-to-value mapping dictionary:

```csharp
var columnMapping = new Dictionary<string, Func<Product, DbValue>>
{
    { "id", x => x.Id },              // Implicit conversion for common types
    { "name", x => x.Name },
    { "price", x => x.Price },
    { "created_at", x => x.CreatedAt }
};
```

`DbValue` has implicit conversions from `string`, `bool`, `short`, `int`, `long`, `float`, `double`, `DateTime`, `DateTimeOffset`, `DateOnly`, and `TimeOnly`. For explicit type control, use the constructor:

```csharp
{ "metadata", x => new DbValue(x.Metadata, NpgsqlDbType.Jsonb) }
```

### Bulk Copy

Insert large amounts of data efficiently using PostgreSQL's COPY protocol:

```csharp
var products = GetProducts(); // IEnumerable<Product>

await db.Bulk.CopyAsync("products", products, columnMapping);
```

For streaming scenarios, use a copy session:

```csharp
var session = await db.Bulk.BeginCopyAsync<Product>("products", columnMapping);

foreach (var product in GetProductStream())
{
    await session.WriteAsync(product);
}

await session.CompleteAsync();
```

### Insert or Update (Upsert)

Performs a bulk upsert. Items that match an existing row (by conflict column) are updated; new items are inserted. Rows where no values actually changed are excluded from the result.

```csharp
var results = await db.Bulk.InsertOrUpdateAsync(
    "products",
    onConflictColumn: "sku",
    items: products,
    columnValueMapping: columnMapping
);

foreach (var result in results)
{
    if (result.IsInserted)
        Console.WriteLine($"Inserted: {result.Item.Name}");
    else if (result.IsUpdated)
        Console.WriteLine($"Updated: {result.Item.Name}");
}
```

For composite unique constraints, pass multiple conflict columns:

```csharp
var results = await db.Bulk.InsertOrUpdateAsync(
    "order_items",
    onConflictColumns: ["order_id", "product_id"],
    items: orderItems,
    columnValueMapping: orderItemMapping
);
```

For partial unique indexes, use `UpsertConfiguration` with a WHERE clause:

```csharp
var config = new UpsertConfiguration
{
    OnConflictColumns = ["email"],
    OnConflictWhereClause = "deleted_at IS NULL"  // Do not include the WHERE keyword
};

var results = await db.Bulk.InsertOrUpdateAsync("users", config, users, columnMapping);
```

### Insert or Skip

Insert items that don't already exist, silently skipping conflicts. Returns only the newly inserted items.

```csharp
var inserted = await db.Bulk.InsertOrSkipAsync(
    "products",
    onConflictColumn: "sku",
    items: products,
    columnValueMapping: columnMapping
);

Console.WriteLine($"Inserted {inserted.Count()} new products");
```

### How Bulk Upsert/Skip Works

Under the hood, `InsertOrUpdateAsync` and `InsertOrSkipAsync`:

1. Create a temporary table with the same structure as the target table.
2. COPY all items into the temp table via binary COPY.
3. Run `INSERT INTO ... SELECT ... ON CONFLICT DO UPDATE SET ...` (or `DO NOTHING` for skip) from the temp table into the target table.
4. Return the affected rows.

For upserts, an `IS DISTINCT FROM` clause ensures that rows where no values actually changed are not counted as updates.

## Database Management

Check for the existence of schemas and tables:

```csharp
bool exists = await db.Management.TableExistsAsync("public", "users");
bool schemaExists = await db.Management.SchemaExistsAsync("analytics");
```

### Schema Extraction

Extract the full database schema as an idempotent SQL script, or query individual schema objects programmatically. All methods exclude system schemas (`pg_catalog`, `information_schema`, `pg_toast`) and the `mvdmio` migration tracking schema.

#### Generate a Schema Script

```csharp
var script = await db.Management.GenerateSchemaScriptAsync();
File.WriteAllText("schema.sql", script);
```

The generated script includes extensions, schemas, enum/composite/domain types, sequences, tables, constraints, indexes, functions/procedures, triggers, and views. The script header includes the current migration version (identifier and name) if migrations have been executed. All statements use idempotent syntax (`IF NOT EXISTS`, `CREATE OR REPLACE`, etc.) so the script can be run repeatedly against the same database without errors.

#### Query Individual Schema Objects

The `db.Management.Schema` property exposes methods for querying individual schema components:

```csharp
var schemas = await db.Management.Schema.GetUserSchemasAsync();
var tables = await db.Management.Schema.GetTablesAsync();
var enums = await db.Management.Schema.GetEnumTypesAsync();
var sequences = await db.Management.Schema.GetSequencesAsync();
var constraints = await db.Management.Schema.GetConstraintsAsync();
var indexes = await db.Management.Schema.GetIndexesAsync();
var functions = await db.Management.Schema.GetFunctionsAsync();
var triggers = await db.Management.Schema.GetTriggersAsync();
var views = await db.Management.Schema.GetViewsAsync();
var extensions = await db.Management.Schema.GetExtensionsAsync();
var compositeTypes = await db.Management.Schema.GetCompositeTypesAsync();
var domainTypes = await db.Management.Schema.GetDomainTypesAsync();
var currentMigration = await db.Management.Schema.GetCurrentMigrationVersionAsync();
```

Each method returns strongly-typed objects. For example, `GetTablesAsync()` returns `TableInfo` objects with full column details including data types, nullability, defaults, and identity generation.

## PostgreSQL LISTEN/NOTIFY

Wait for notifications on a PostgreSQL channel:

```csharp
// Wait indefinitely
await db.WaitAsync("order_updates", cancellationToken);

// Wait with a timeout
await db.WaitAsync("order_updates", TimeSpan.FromSeconds(30), cancellationToken);
```

A separate dedicated connection is used for notifications, so they don't interfere with regular queries.

## Migrations

### Creating Migrations

Migrations implement `IDbMigration` with a timestamp-based identifier:

```csharp
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

public class AddUsersTable : IDbMigration
{
   public long Identifier => 202602161430;  // YYYYMMDDHHmm format
   public string Name => "AddUsersTable";

   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         CREATE TABLE users (
            id SERIAL PRIMARY KEY,
            name TEXT NOT NULL,
            email TEXT NOT NULL UNIQUE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
         )
         """
      );
   }
}
```

### Running Migrations Programmatically

```csharp
using mvdmio.Database.PgSQL.Migrations;

await using var db = new DatabaseConnection("Host=localhost;Database=mydb;...");
var migrator = new DatabaseMigrator(db, typeof(AddUsersTable).Assembly);

// Run all pending migrations
await migrator.MigrateDatabaseToLatestAsync();

// Or migrate to a specific version
await migrator.MigrateDatabaseToAsync(202602161430);

// Check which migrations have been executed
var executed = await migrator.RetrieveAlreadyExecutedMigrationsAsync();
```

Each migration runs in its own transaction. If a migration fails, its transaction is rolled back and a `MigrationException` is thrown. Executed migrations are tracked in the `mvdmio.migrations` table (automatically created).

### CLI Tool

The `mvdmio.Database.PgSQL.Tool` package provides a `db` CLI tool for managing migrations and extracting schema:

```bash
# Install the tool
dotnet tool install --global mvdmio.Database.PgSQL.Tool

# Initialize the configuration file
db init

# Create a new migration (generates a timestamped file)
db migration create AddUsersTable

# Run all pending migrations (uses the first configured environment)
db migrate latest

# Run migrations against a specific environment
db migrate latest --environment prod
db migrate latest -e acc

# Run migrations up to a specific version
db migrate to 202602161430

# Override connection string directly
db migrate latest --connection-string "Host=localhost;Database=mydb;..."

# Pull the current database schema into a schema.<env>.sql file
db pull

# Pull from a specific environment
db pull --environment prod
db pull -e acc

# Pull using an explicit connection string (produces schema.sql)
db pull --connection-string "Host=localhost;Database=mydb;..."
```

#### Configuration

Create a `.mvdmio-migrations.yml` file in your project root:

```yaml
project: src/MyApp.Data          # Path to the project containing migrations
migrationsDirectory: Migrations  # Directory for migration files (default: Migrations)
schemasDirectory: Schemas        # Directory for schema files from db pull (default: Schemas)
connectionStrings:               # The first entry is used when no --environment flag is passed
  local: Host=localhost;Database=mydb;Username=postgres;Password=secret
  acc: Host=acc-server;Database=mydb;Username=postgres;Password=secret
  prod: Host=prod-server;Database=mydb;Username=postgres;Password=secret
```

Migrations are tracked in the `mvdmio.migrations` table (automatically created).

#### Multi-Environment Support

The `connectionStrings` section maps environment names to connection strings. Use the `--environment` (or `-e`) flag to select which environment to run against:

```bash
# Use the first configured environment (default behavior)
db migrate latest

# Target a specific environment
db migrate latest --environment prod
db migrate to 202602161430 -e acc

# Override the connection string entirely (ignores environments)
db migrate latest --connection-string "Host=custom;Database=mydb;..."
```

Connection string resolution priority:
1. `--connection-string` flag (explicit override)
2. `--environment` / `-e` flag (looks up from `connectionStrings` in config)
3. First entry in `connectionStrings` (fallback)
4. Error if none of the above resolve

#### Schema-First Migrations

When migrating an empty database (one with no migrations applied), the migrator can automatically apply an embedded schema file instead of running all migrations individually. This significantly speeds up provisioning new database instances.

**Automatic embedding:**

The NuGet package includes a `.props` file that automatically embeds any `.sql` files in your `Schemas/` folder as assembly resources. No manual configuration is needed - just place schema files in the `Schemas/` folder and they are embedded on build.

**How it works:**

1. When `db migrate latest` or `db migrate to <version>` is run, the migrator checks if the database is empty (no migrations applied).
2. If empty, it scans the migrations assembly for embedded schema resources:
   - For environment-based runs: looks for `schema.<environment>.sql` (case-insensitive, e.g., `schema.local.sql`)
   - Falls back to `schema.sql` if no environment-specific file is found
   - If no environment specified, picks the first available schema
3. If an embedded schema is found, it is applied instead of running individual migrations.
4. The migration version from the schema file header is recorded in the migrations table.
5. Any migrations newer than the schema version are then applied normally.

**Generating schema files:**

Use `db pull` to generate a schema file from an existing database:

```bash
# Pull schema from the default environment
db pull

# Pull from a specific environment
db pull --environment prod
```

The generated schema file is written to the `Schemas/` directory and includes a header comment with the current migration version:

```sql
--
-- PostgreSQL database schema
-- Generated at 2026-02-18 10:30:45 UTC
-- Migration version: 202602161430 (AddUsersTable)
--

CREATE TABLE users (...);
-- ... rest of schema
```

On the next build, this file is automatically embedded as an assembly resource.

**Workflow example:**

```bash
# On your production database (after migrations are applied)
db pull --environment prod
# Creates Schemas/schema.prod.sql

# Build the project to embed the schema
dotnet build

# On a new development database
db migrate latest --environment local
# Detects empty database, discovers embedded schema.local.sql,
# applies it, then runs any migrations newer than the schema version
```

**Programmatic usage:**

```csharp
using mvdmio.Database.PgSQL.Migrations;

await using var db = new DatabaseConnection(connectionString);

// Specify an environment to select the appropriate embedded schema
// Looks for schema.local.sql, falls back to schema.sql
var migrator = new DatabaseMigrator(
    db,
    environment: "local",
    typeof(AddUsersTable).Assembly
);

// This will:
// 1. Check if the database is empty
// 2. If empty, discover and apply the embedded schema file
// 3. Record the migration version from the schema file header
// 4. Run any remaining migrations after the schema version
await migrator.MigrateDatabaseToLatestAsync();

// Or migrate to a specific version (schema is only applied if its version <= target)
await migrator.MigrateDatabaseToAsync(202602161430);
```

Without an environment, the migrator picks the first available embedded schema:

```csharp
// Uses first available embedded schema, or runs migrations if none found
var migrator = new DatabaseMigrator(db, typeof(AddUsersTable).Assembly);
await migrator.MigrateDatabaseToLatestAsync();
```

The `db migration create` command scaffolds a migration file with the correct namespace and timestamp:

```csharp
// Generated file: _202602161430_AddUsersTable.cs
using mvdmio.Database.PgSQL;
using mvdmio.Database.PgSQL.Migrations.Interfaces;

namespace MyApp.Data.Migrations;

public class _202602161430_AddUsersTable : IDbMigration
{
   public long Identifier { get; } = 202602161430;
   public string Name { get; } = "AddUsersTable";

   public async Task UpAsync(DatabaseConnection db)
   {
      await db.Dapper.ExecuteAsync(
         """
         -- TODO: Write your migration SQL here
         """
      );
   }
}
```

## Type Handlers

The library includes Dapper type handlers that are registered automatically when using `DatabaseConnectionFactory`:

| C# Type | PostgreSQL Type | Notes |
|---------|----------------|-------|
| `DateOnly` | `DATE` | Workaround for Dapper not natively supporting `DateOnly` |
| `TimeOnly` | `TIME` | Workaround for Dapper not natively supporting `TimeOnly` |
| `Uri` | `TEXT` | Stored as absolute URI string |
| `Dictionary<string, string>` | `JSONB` | Serialized/deserialized via System.Text.Json |

### Custom Type Handlers

Register your own types for JSONB storage:

```csharp
using Dapper;
using mvdmio.Database.PgSQL.Dapper.TypeHandlers.Base;

// Store any type as JSONB
SqlMapper.AddTypeHandler(new JsonbTypeHandler<MyComplexType>());

// Store enums as strings
SqlMapper.AddTypeHandler(new EnumAsStringTypeHandler<OrderStatus>());
```

### Typed Query Parameters

When you need to specify an explicit PostgreSQL type for a query parameter:

```csharp
using mvdmio.Database.PgSQL.Dapper.QueryParameters;

var parameters = new Dictionary<string, object?>
{
    ["data"] = new TypedQueryParameter(jsonString, NpgsqlDbType.Jsonb)
};

await db.Dapper.ExecuteAsync("INSERT INTO events (data) VALUES (:data)", parameters);
```

## Error Handling

The library provides specific exception types:

- **`QueryException`** -- Thrown when a SQL query fails. Contains the `Sql` property with the failed query text.
- **`MigrationException`** -- Thrown when a migration fails. Contains the `Migration` property with the migration that failed.
- **`DatabaseException`** -- Base exception for all database-related errors.

```csharp
try
{
    await db.Dapper.ExecuteAsync("INVALID SQL");
}
catch (QueryException ex)
{
    Console.WriteLine($"Query failed: {ex.Sql}");
    Console.WriteLine($"Cause: {ex.InnerException?.Message}");
}
```

## License

MIT -- see [LICENSE](LICENSE) for details.
