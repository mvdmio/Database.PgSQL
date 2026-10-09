# 01 — Cap and name every pool by default

Status: done
Depends on: none

## What to build

After this step, every data source the library builds gets a pool cap and a name, with no code change by the consumer:

- **Where:** data sources built by `DatabaseConnectionFactory.BuildConnection` and `BuildDataSource`, and the data source each `DatabaseConnection` constructor that takes a connection string builds for itself. Both constructors count: `DatabaseConnection(string)` and `DatabaseConnection(string, Action<NpgsqlDataSourceBuilder>)`.
- **Cap:** `Maximum Pool Size` is 10. Npgsql's own default is 100.
- **Name:** the entry assembly's simple name (`Assembly.GetEntryAssembly()?.GetName().Name`). When there is no entry assembly, the name stays unset, and Npgsql behaves as it does today.
- **Two places:** the name sets the connection string's `Application Name`, which Postgres shows in `pg_stat_activity.application_name`. It also sets `NpgsqlDataSourceBuilder.Name`, which Npgsql uses as the pool name in traces, logs and metrics. Postgres cuts a name longer than 63 bytes; the library does not shorten it.
- **Connection string keywords win over the defaults,** each value on its own. A keyword counts under any spelling Npgsql accepts for it, in any letter case. Npgsql 10.0.1 accepts `Maximum Pool Size` and `MaxPoolSize` for the cap, and `Application Name` and `ApplicationName` for the name. A keyword that is present wins whatever its value. When the name comes from a keyword, `NpgsqlDataSourceBuilder.Name` gets that value too, so the pool name and `application_name` always agree. Leave `Name` unset only when the name that wins is empty or missing.
- **The per-call `Action<NpgsqlDataSourceBuilder>` runs after the defaults,** so it can still override them.
- **The factory keeps its other settings:** `IncludeErrorDetail`, `LogParameters` and dynamic JSON. The cache key stays the caller's connection string. The direct `DatabaseConnection` path keeps only dynamic JSON. Do not add `IncludeErrorDetail` or `LogParameters` to it.
- **One shared rule:** put the rule that combines keywords and defaults in one internal place that both paths call, so the factory and the direct constructor cannot drift apart. Make it a pure function: its inputs are the caller's connection string and the entry assembly name (passed in, so a missing entry assembly can be tested), and its outputs are the cap and the name to apply. Step 02 adds explicit factory settings as a third input with the highest precedence, so leave room for that input.
- **No library checks:** a connection string whose `Minimum Pool Size` is above the cap fails when the data source is built, with Npgsql's own error. The library does not raise the cap to match.
- **Version:** bump `<PgSqlVersion>` from 0.40.0 to 0.41.0. A default that changes behaviour for every consumer is a breaking change, and on 0.x a breaking change bumps the minor version. Steps 02 and 03 ship in the same release and do not bump it again.
- **README:** update `src/mvdmio.Database.PgSQL/README.md`:
  - In "Connections" and "Connection Factory", give the default cap and name and say that a connection string keyword beats them.
  - Say that the default cap changed from 100 to 10, and why: several programs that share one Postgres server can otherwise ask for more connections than `max_connections` allows.
  - Give the fix for a pool-exhausted error after upgrading: set `Maximum Pool Size` in the connection string.
  - Say that the name shows in `pg_stat_activity` and as Npgsql's pool name.
  - Remove a stale claim while you rewrite that paragraph: the README says the plain `new DatabaseConnection(connectionString)` lacks dynamic JSON. Both paths enable it now.
- **No CHANGELOG entry in this step.** `/document-changes` writes the CHANGELOG entry the Spec asks for after review. That entry names the 100-to-10 change and its reason.

## Footprint

Projects: mvdmio.Database.PgSQL, mvdmio.Database.PgSQL.Tool, mvdmio.Database.PgSQL.Tests.Unit, mvdmio.Database.PgSQL.Tests.Integration, mvdmio.Database.PgSQL.Tests.Integration.OData, mvdmio.Database.PgSQL.Tests.Packaging (needs Docker and network)

- `src/mvdmio.Database.PgSQL/DatabaseConnectionFactory.cs` — `RetrieveOrCreate`, class XML docs, `BuildConnection` and `BuildDataSource` docs
- `src/mvdmio.Database.PgSQL/DatabaseConnection.cs` — private static `BuildDataSource` and its remarks; docs of `DatabaseConnection(string)` and `DatabaseConnection(string, Action<NpgsqlDataSourceBuilder>)`
- `src/mvdmio.Database.PgSQL/Internal/` — new file for the shared pure rule (keywords and defaults to cap and name)
- `test/mvdmio.Database.PgSQL.Tests.Integration/ConnectionPoolTests.cs` — new class beside `DataSourceConstructionTests.cs`, built against `TestFixture` the same way
- `test/mvdmio.Database.PgSQL.Tests.Integration/Fixture/TestFixture.cs` — `DbContainer.GetConnectionString()` (read only)
- `test/mvdmio.Database.PgSQL.Tests.Unit/Internal/` — new unit tests for the shared rule
- `test/mvdmio.Database.PgSQL.Tests.Unit/DatabaseConnectionFactoryTests.cs` — the per-call action override
- `Directory.Build.props` — `<PgSqlVersion>`
- `src/mvdmio.Database.PgSQL/README.md` — "Connections" and "Connection Factory" sections

## Acceptance criteria

- [ ] Integration: a connection from `new DatabaseConnectionFactory()` shows the entry assembly's name in `pg_stat_activity.application_name`. Read it for the connection's own backend (`pid = pg_backend_pid()`). Under xUnit v3 the test project runs as its own executable, so expect the integration test assembly's name, and confirm that before asserting it as a literal.
- [ ] Integration: with the factory defaults and a `Timeout=1` keyword in the test's connection string, 10 connections open and stay open at once. The 11th open waits and then fails with Npgsql's pool-exhausted `NpgsqlException`.
- [ ] Integration: with no factory setting, a connection string with a `Maximum Pool Size` keyword and an `Application Name` keyword beats the defaults for both values. `application_name` shows the keyword's name, and the pool is exhausted at the keyword's cap, not at 10. Use a small keyword cap, such as 3, so the test holds few slots on the shared container.
- [ ] Integration: a connection from `new DatabaseConnection(connectionString)` shows the entry assembly's name in `pg_stat_activity.application_name`, and its data source has a cap of 10. One `DatabaseConnection` holds one connection, so read the cap from what Npgsql reports, not by exhausting the pool. Two ways to read it: the connection string Npgsql reports for the open connection, or Npgsql's `db.client.connection.max` metric.
- [ ] Integration: the data source's diagnostic name equals the pool's name. Npgsql's metrics carry the name in the `db.client.connection.pool.name` tag. Use a unique name from an `Application Name` keyword so other tests' pools cannot match.
- [ ] A per-call `Action<NpgsqlDataSourceBuilder>` that sets the cap or the name on the builder's `ConnectionStringBuilder` overrides the default. The built data source's `ConnectionString` shows the action's value.
- [ ] Unit tests of the shared rule cover:
  - Each keyword spelling, in mixed letter case.
  - A keyword beats the default, separately for the cap and the name.
  - No entry assembly leaves the name unset.
  - Neither present gives a cap of 10 and the entry assembly's name.
- [ ] The existing `DataSourceConstructionTests` and `DatabaseConnectionFactoryTests` still pass unchanged.
- [ ] `<PgSqlVersion>` is 0.41.0.
- [ ] The package README states the new defaults, the precedence, the 100-to-10 change and why, and the `Maximum Pool Size` fix.
- [ ] `dotnet format --verify-no-changes` exits zero, `dotnet build` succeeds, and the tests of every listed project pass, run one after another.

## Outcome

Safety fact: every data source the library builds — factory and both `DatabaseConnection(string, ...)` constructors — caps its pool at 10 and names it after the entry assembly unless a connection string keyword or a per-call builder action says otherwise; if false, pools grow to Npgsql's 100 and connections show an empty `application_name`, which is the 2026-10-04 `53300` incident again (rung 3)
Proof: `dotnet test test/mvdmio.Database.PgSQL.Tests.Integration/mvdmio.Database.PgSQL.Tests.Integration.csproj --filter "FullyQualifiedName~ConnectionPoolTests|FullyQualifiedName~DataSourceConstructionTests"` exit 0 — Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9 (the 11th open on a default factory pool throws "pool has been exhausted"; `pg_stat_activity.application_name` reads `mvdmio.Database.PgSQL.Tests.Integration`)
Merge risk: easy — reverting the commit restores Npgsql's defaults; nothing is persisted; affects every consumer that upgrades to 0.41.0 (a program that needs more than 10 concurrent connections per pool hits pool exhaustion until it sets `Maximum Pool Size`)

Notes for later steps:
- The shared rule is `src/mvdmio.Database.PgSQL/Internal/PoolDefaults.cs`: `PoolDefaults.Resolve(string connectionString, string? entryAssemblyName)` returns `PoolSettings(int MaxPoolSize, string? Name)`; `PoolDefaults.Apply(NpgsqlDataSourceBuilder builder, string connectionString)` reads the entry assembly and applies the result (sets `ConnectionStringBuilder.MaxPoolSize`, and when the name is not null also `ApplicationName` and `builder.Name`). Step 02 adds the explicit factory settings as a third input to `Resolve` and `Apply`, with highest precedence.
- Drift: an empty keyword (`Application Name=`) counts as absent, not as "present whatever its value". ADO.NET's `DbConnectionStringBuilder` — which Npgsql's own parsing uses — drops a keyword with an empty value, so neither the library nor Npgsql can see it. The default name applies in that case; pinned by `PoolDefaultsTests.Resolve_WithAnEmptyNameKeyword_TreatsItAsAbsent`.
- The entry assembly under xUnit v3 is confirmed as `mvdmio.Database.PgSQL.Tests.Integration`. Npgsql 10's metric tag is `db.client.connection.pool.name` on instrument `db.client.connection.max`; `ConnectionPoolTests.ObserveMaxConnectionsByPoolName` reads it with a `MeterListener`.
- README: a new "Pool Size and Name" subsection under "Connections" carries the defaults, precedence, the 100-to-10 change and the fix; "Connection Factory" links to it. "Dependency Injection" is untouched (Step 03's).
- Checker: an action that sets only `ConnectionStringBuilder.ApplicationName` leaves `builder.Name` (the pool name) at the default; the README says so. `ConnectionPoolTests` now also covers the per-call action on the direct `DatabaseConnection` constructor, and `.agents/refs/testing.md` records that pool tests need not inherit `TestBase`. Step 02 should keep the `DatabaseConnection` constructor docs pointing at `DatabaseConnectionFactory` rather than restating the defaults.
