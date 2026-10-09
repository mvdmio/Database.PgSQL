# Cap and name every connection pool by default

## Problem Statement

Every Npgsql data source this library builds uses Npgsql's defaults. A pool may grow to 100 connections, and its connections carry no application name.

A program that runs several pools against one Postgres server can therefore ask for far more connections than the server allows. The mvdmio suite did exactly that. Six apps share one Postgres server with `max_connections` at 100. Each app process has two pools, its own and ASP.Jobs' pool, and each pool is allowed 100 connections. On 2026-10-04 the server refused new connections with `53300: sorry, too many clients already`.

Nobody could tell afterwards which program held the slots. Every connection in `pg_stat_activity` showed the same user, the same client address, and an empty `application_name`.

The library already has a per-call hook, the optional `Action<NpgsqlDataSourceBuilder>` on `BuildConnection` and `BuildDataSource`. It cannot carry a cap or a name reliably. `DatabaseConnectionFactory` caches one data source per connection string, so only the first caller's action takes effect, and a later caller's action is silently ignored. In the suite, a shared client package often builds the first connection, so an app's own setting would be lost.

## Solution

Every data source the library builds gets a pool cap and a name by default:

- The cap is 10 connections.
- The name is the program's entry assembly name, for example `mvdmio.Compliance.Web`.

A consumer can set either value once, on the connection factory, and the setting then applies to every data source that factory builds. A `Maximum Pool Size` or `Application Name` keyword in the connection string still works and beats the default. The name also becomes the data source's diagnostic name, so Npgsql's traces and metrics say which pool a span or measurement came from.

## User Stories

1. As an operator of a Postgres server shared by several programs, I want every program's pools capped by default, so that one program cannot take every connection slot.
2. As an operator, I want each connection to show which program opened it in `pg_stat_activity.application_name`, so that I can see who holds the slots during an incident.
3. As a developer upgrading the library, I want the cap and the name to work without any code change, so that every program I upgrade gets capped, named pools.
4. As a developer, I want to set the cap and the name once on the connection factory, so that they apply to every data source the factory builds, whichever caller builds first.
5. As a developer registering the factory through dependency injection, I want `AddDatabase` to accept the same settings, so that I don't have to register the factory by hand.
6. As a developer whose tests or tools need more connections, I want a `Maximum Pool Size` keyword in my connection string to beat the library's default, so that those programs keep working without code changes.
7. As a developer who sets an explicit factory setting, I want it to beat the connection string, so that code can enforce a value whatever configuration supplies.
8. As a developer who uses `new DatabaseConnection(connectionString)`, I want the same defaults on the data source it builds for itself, so that no pool the library builds escapes the cap.
9. As the author of a library that builds its own factory, such as ASP.Jobs, I want to give my pool its own name, so that its connections can be told apart from the host app's connections.
10. As a developer reading traces, I want the data source's diagnostic name to equal the pool's name, so that database spans say which pool they came from.
11. As a developer whose program has no entry assembly, I want the library to leave the name unset rather than fail, so that unusual hosts keep working.
12. As a developer who hits a pool-exhausted error after upgrading, I want the README and the CHANGELOG to say that the default cap changed from 100 to 10 and why, so that I can find the cause and the fix quickly.
13. As a developer who passes a per-call `Action<NpgsqlDataSourceBuilder>`, I want it to still run after the defaults are applied, so that my existing code keeps its last word on the builder.

## Implementation Decisions

- **A settings type for the connection factory.** It carries two optional values: the pool cap (maximum pool size) and the name. `DatabaseConnectionFactory` gains a constructor that takes the settings. The parameterless constructor stays, and it means "use the defaults". Today it is the implicit one, so it must now be declared explicitly.
- **`AddDatabase` gains an overload** that takes the settings and registers a factory built with them. It replaces any factory already registered, so call order doesn't matter: the generated `AddXxx()` methods call the existing `AddDatabase()`, which uses `TryAdd` and so does nothing once a factory is registered. The existing overload keeps registering a factory with the defaults, through `TryAdd` as today. If two callers pass different settings, the last call wins, and the README says so.
- **Defaults.**
  - The cap is 10 connections. A web app built on this library returns each connection to the pool as soon as its statement ends, so 10 covers ordinary use. A program that needs more says so.
  - The name is the entry assembly's simple name. When there is no entry assembly, the name stays unset.
- **Precedence, applied to each value on its own.** An explicit factory setting wins. Next comes a keyword in the connection string, recognised under any of its Npgsql aliases. The library default applies only when neither is present.
- **Where the settings apply.**
  - They apply to every data source the factory builds and caches. The cache key stays the caller's connection string, so callers and the cache behave as before.
  - They also apply to the data source that each `DatabaseConnection` constructor taking a connection string builds for itself. Those constructors use the defaults; they take no settings.
- **One name, two places.** The name sets both the connection string's `Application Name`, which Postgres shows in `pg_stat_activity`, and the data source builder's `Name`, which Npgsql uses for tracing, logging and metrics. Postgres cuts an `application_name` longer than 63 bytes; the library does not shorten it itself.
- **The per-call `Action<NpgsqlDataSourceBuilder>` runs after the defaults and settings,** so it can still override them for the caller that builds the data source first. Its caching behaviour is unchanged.
- **The settings already applied by the factory stay:** `IncludeErrorDetail`, `LogParameters` and dynamic JSON.
- **Versioning.** The default changes behaviour for every consumer that upgrades, so this is a breaking change: bump the minor version of `<PgSqlVersion>` in `Directory.Build.props` (now 0.40.0, so 0.41.0), as the repo's rules require while on 0.x. Update the package README's (`src/mvdmio.Database.PgSQL/README.md`) "Connection Factory" and "Dependency Injection" sections, and add a CHANGELOG entry that names the new default and the reason for it.

## Testing Decisions

- A good test opens real connections against a real Postgres and checks what Postgres and Npgsql show from outside. It doesn't inspect private fields of the factory.
- Integration tests against the library's Testcontainers Postgres, following the existing tests in `test/mvdmio.Database.PgSQL.Tests.Integration/DataSourceConstructionTests.cs`, which build both kinds of connection against `TestFixture`, cover:
  - A factory with the defaults: a connection shows the entry assembly's name in `pg_stat_activity.application_name`, and the 11th concurrent open waits and then fails with Npgsql's pool-exhausted error. Use a short `Timeout` keyword in the test's connection string so the wait stays short.
  - An explicit factory setting: the name and the cap follow the setting, even when the connection string has different keywords.
  - A connection string keyword with no factory setting: the keyword wins over the default, for both the cap and the name.
  - `new DatabaseConnection(connectionString)`: the default name and cap apply.
  - `AddDatabase` with settings: the resolved factory uses them, whether the plain `AddDatabase()` ran before it or after it.
- Unit tests may cover how the settings, the keywords and the defaults combine, if that logic sits in a pure function. Unit tests are optional; the integration tests are the proof.

## Out of Scope

- Changing how the per-call `Action<NpgsqlDataSourceBuilder>` interacts with the data source cache.
- Npgsql's `Timeout`, the wait for a free connection, which stays at 15 seconds.
- Exporting Npgsql metrics; that is each consumer's choice.
- Any Postgres server setting, such as `max_connections`.
- Changes in consumers. ASP.Jobs names its own pool in its own Issue, and the mvdmio suite takes the new version in mvdmio-suite #263.

## Further Notes

- Origin: the grilling of mvdmio-suite #263 and #264 on 2026-10-08 and 2026-10-09. The suite chose capped, named pools over raising `max_connections`. Raising the server limit would only delay the same fault until usage grows.
- Known consumers: mvdmio-suite (0.39.1), ASP.Jobs (requires at least 0.29.0, so the suite's version wins), and mvdm-mko-game (0.30.0). Each picks up the new defaults when it upgrades.
- The mvdmio suite's test fixtures run 16–32 tests at once on one pool, and each test holds its connection for its whole run. The suite will set `Maximum Pool Size` in its test connection string, which is the case user story 6 covers.

