# 02 — Set the cap and the name once on the factory

Status: built
Depends on: 01

## What to build

A consumer can set the pool cap and the name once, on the connection factory. The settings then apply to every data source that factory builds and caches, whichever caller builds first.

- **Settings type:** add a new public type for the factory's settings. The name is a guess the step agent may refine: `DatabaseConnectionFactorySettings`. It is a `[PublicAPI]` sealed class with XML docs and two optional init-only values:
  - `int? MaxPoolSize`, the pool cap.
  - `string? ApplicationName`, the name.

  A `null` value means "not set". A value that is set is used exactly as given.
- **Constructors:**
  - `DatabaseConnectionFactory(DatabaseConnectionFactorySettings settings)` builds every data source with the settings. A `null` argument throws `ArgumentNullException`.
  - The parameterless constructor stays and means "use the defaults". It is implicit today, so declare it explicitly.
- **Precedence, each value on its own:** an explicit factory setting wins. Next comes a connection string keyword under any spelling Npgsql accepts (step 01's rule). The library default applies only when neither is present. An explicit name also becomes `NpgsqlDataSourceBuilder.Name`, so the pool name and `application_name` agree.
- **Where it plugs in:** extend step 01's shared rule with the explicit settings as its highest-precedence input. The factory passes its settings. The direct `DatabaseConnection` constructors pass none: they take no settings and keep the defaults.
- **Unchanged:** the per-call `Action<NpgsqlDataSourceBuilder>` still runs last. The cache key stays the caller's connection string. `IncludeErrorDetail`, `LogParameters` and dynamic JSON stay on.
- **No library checks:** a value Npgsql refuses, such as a negative cap, fails when the data source is built, with Npgsql's own error.
- **DI must keep working:** `AddDatabase()` registers the factory by type through `TryAddSingleton`. With two public constructors, Microsoft's container must still pick the parameterless one, because the settings type is not registered. Keep `ServiceCollectionExtensionsTests` green. Step 03 adds the settings overload of `AddDatabase`.
- **README:** in the "Connection Factory" section of `src/mvdmio.Database.PgSQL/README.md`:
  - Show the settings on the factory constructor.
  - State the precedence: setting, then keyword, then default.
  - Say why the factory is the place to set them: the per-call action only takes effect for the caller that builds the data source first.
  - Say that a library that builds its own factory can give its pool its own name.

## Footprint

Projects: mvdmio.Database.PgSQL, mvdmio.Database.PgSQL.Tests.Unit, mvdmio.Database.PgSQL.Tests.Integration

- `src/mvdmio.Database.PgSQL/DatabaseConnectionFactorySettings.cs` — new public settings type
- `src/mvdmio.Database.PgSQL/DatabaseConnectionFactory.cs` — new constructors, a stored settings field, `RetrieveOrCreate`, class XML docs
- `src/mvdmio.Database.PgSQL/Internal/` — step 01's shared rule gains the explicit-settings input
- `src/mvdmio.Database.PgSQL/DatabaseConnection.cs` — private static `BuildDataSource`: passes no settings to the shared rule
- `test/mvdmio.Database.PgSQL.Tests.Integration/ConnectionPoolTests.cs` — explicit-setting tests (the file step 01 created)
- `test/mvdmio.Database.PgSQL.Tests.Unit/Internal/` — step 01's rule tests, extended with explicit settings
- `test/mvdmio.Database.PgSQL.Tests.Unit/DatabaseConnectionFactoryTests.cs` — constructor guard, settings applied regardless of the per-call action
- `test/mvdmio.Database.PgSQL.Tests.Unit/ServiceCollectionExtensionsTests.cs` — must stay green (read only)
- `src/mvdmio.Database.PgSQL/README.md` — "Connection Factory" section

## Acceptance criteria

- [ ] Integration: a factory built with an explicit cap and name, given a connection string whose `Maximum Pool Size` and `Application Name` keywords differ, shows the setting's name in `pg_stat_activity.application_name`. Its pool is exhausted at the setting's cap. Use a short `Timeout` keyword and small caps, for example a setting of 2 against a keyword of 5.
- [ ] Integration: Npgsql's `db.client.connection.pool.name` metric tag equals the setting's name. Use a unique name.
- [ ] Each value follows the precedence on its own:
  - A factory that sets only the name keeps the keyword's cap, or 10 when there is no keyword.
  - A factory that sets only the cap keeps the keyword's name, or the entry assembly's name when there is no keyword.

  Unit tests of the shared rule may prove this.
- [ ] The settings apply to a data source first built through `BuildDataSource` or `BuildConnection` with or without a per-call action. A per-call action that sets a value still overrides the setting.
- [ ] `new DatabaseConnectionFactory(null!)` throws `ArgumentNullException`.
- [ ] `new DatabaseConnectionFactory()` behaves exactly as in step 01. Step 01's tests still pass.
- [ ] `ServiceCollectionExtensionsTests` still pass.
- [ ] The package README shows the settings, the precedence, and why to set them on the factory.
- [ ] `dotnet format --verify-no-changes` exits zero, `dotnet build` succeeds, and the tests of every listed project pass, run one after another.

## Outcome

Safety fact: a `DatabaseConnectionFactory` built with `DatabaseConnectionFactorySettings` gives every data source it builds the setting's cap and name — over the connection string's keywords and the defaults, each value on its own, whether or not the first caller passes a per-call action — so `pg_stat_activity.application_name` and Npgsql's pool name show the setting and the pool is exhausted at the setting's cap; if false, an app's or library's own cap and name are silently lost to whichever caller built the data source first, the gap the Spec names (rung 3)
Proof: `dotnet test test/mvdmio.Database.PgSQL.Tests.Integration/mvdmio.Database.PgSQL.Tests.Integration.csproj --filter "FullyQualifiedName~ConnectionPoolTests|FullyQualifiedName~DataSourceConstructionTests"` exit 0 — Passed: 12, Failed: 0 (incl. `DataSourceFromAFactoryWithSettings_SettingsBeatTheKeywords`: setting 2 vs keyword 5, exhausted at 2, setting's name in `pg_stat_activity`; `..._PublishesItsCapUnderTheSettingsName`: `db.client.connection.pool.name` equals the setting). Precedence per value: `DOTNET_ROLL_FORWARD=Major dotnet test test/mvdmio.Database.PgSQL.Tests.Unit/mvdmio.Database.PgSQL.Tests.Unit.csproj --filter "FullyQualifiedName~PoolDefaultsTests|FullyQualifiedName~DatabaseConnectionFactoryTests|FullyQualifiedName~ServiceCollectionExtensionsTests"` exit 0 — Passed: 40, Failed: 0. The Proof folder refused writes from this worktree-isolated session, so the Proof names the commands.
Merge risk: easy — reverting the commit removes the new public type and constructor; nothing is persisted and no release is published until the run lands; affects consumers that adopt `DatabaseConnectionFactorySettings` (Step 03's `AddDatabase` overload builds on it)

Notes for later steps:
- `DatabaseConnectionFactorySettings` (`src/mvdmio.Database.PgSQL/DatabaseConnectionFactorySettings.cs`): sealed `[PublicAPI]` class, `int? MaxPoolSize { get; init; }`, `string? ApplicationName { get; init; }`. The parameterless factory constructor chains to `DatabaseConnectionFactory(new DatabaseConnectionFactorySettings())`; the settings constructor throws `ArgumentNullException` (param `settings`).
- `PoolDefaults.Resolve(connectionString, entryAssemblyName, settings = null)` and `PoolDefaults.Apply(builder, connectionString, settings = null)` take the settings as the highest-precedence input. `DatabaseConnection`'s private `BuildDataSource` passes `settings: null`.
- Drift: an explicit `ApplicationName = ""` is used as given — it beats the keyword and the default, sets `Application Name` to empty, and leaves `NpgsqlDataSourceBuilder.Name` unset (Step 01's rule: `Name` stays unset when the winning name is empty). Pinned by `PoolDefaultsTests.Resolve_WithAnExplicitEmptyName_KeepsItAsGiven`.
- DI: `ServiceCollectionExtensionsTests` stay green with two public constructors; Microsoft's container picks the parameterless one because the settings type is not registered. Step 03 should register the factory as an instance or a delegate rather than registering `DatabaseConnectionFactorySettings` itself, or `AddDatabase()`'s by-type registration would start picking the settings constructor.
- README: "Connection Factory" now shows the settings, the precedence list, why the factory beats the per-call action, and the own-name-for-a-library point; "Pool Size and Name" links to it. "Dependency Injection" is still untouched (Step 03's).
- The Unit project targets net9.0; on this host prefix its test command with `DOTNET_ROLL_FORWARD=Major`, as `.agents/refs/testing.md` says.
