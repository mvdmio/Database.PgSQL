# 03 — Pass the factory settings through AddDatabase

Status: built
Depends on: 01, 02

## What to build

A consumer who registers the factory through dependency injection can pass the factory settings to `AddDatabase`. The resolved factory then uses those settings, whatever order the registrations run in.

- **New overload:** `AddDatabase(DatabaseConnectionFactorySettings settings)` (use the settings type step 02 named) goes in the same `extension(IServiceCollection services)` block as the plain overload. It:
  - Removes every `DatabaseConnectionFactory` registration already present.
  - Registers a singleton factory built with the settings, through a factory delegate, so the container builds it and disposes it with the provider, as it does the plain overload's factory.
  - Returns the service collection for chaining.
  - Throws `ArgumentNullException` for a `null` argument.
- **Plain overload unchanged:** `AddDatabase()` keeps registering the factory by type through `TryAddSingleton`, with the defaults. Once a factory is registered, it does nothing.
- **Order does not matter:** the generated `AddXxx()` methods call `AddDatabase()`. They are emitted by `GeneratedAssemblyRegistrationSourceBuilder` as `ServiceCollectionExtensions.AddDatabase(services)`, which must stay unambiguous. Calling the settings overload before or after them gives the same result: the settings apply. When the settings overload runs twice with different settings, the last call wins.
- **Test dependency:** an integration test that resolves the factory needs `BuildServiceProvider`, which lives in `Microsoft.Extensions.DependencyInjection`. The integration test project does not reference that package yet. Add it at the version the library uses for `Microsoft.Extensions.DependencyInjection.Abstractions` (10.0.3), and add a row to the Tests table in `.agents/refs/dependencies.md`. Add the same reference to the unit test project only if a unit test resolves a provider.
- **README:** update `src/mvdmio.Database.PgSQL/README.md`:
  - In the "Dependency Injection" section under "Connections", show `AddDatabase(settings)`. Say it replaces any factory already registered, works before or after the generated `AddXxx()` methods, and that the last call wins when two callers pass different settings.
  - In the "Dependency Injection" section under "Generated Repositories", add one sentence that points to it.
- **This is the last step:** it leaves the whole solution green.

## Footprint

Projects: mvdmio.Database.PgSQL, mvdmio.Database.PgSQL.Tool, mvdmio.Database.PgSQL.Analyzers, mvdmio.Database.PgSQL.Tests.Unit, mvdmio.Database.PgSQL.Tests.Integration, mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema, mvdmio.Database.PgSQL.Tests.Integration.OData, mvdmio.Database.PgSQL.Tests.Packaging (needs Docker and network), mvdmio.Database.PgSQL.Analyzers.Tests

- `src/mvdmio.Database.PgSQL/ServiceCollectionExtensions.cs` — the `extension(IServiceCollection services)` block: `AddDatabase()` and the new overload
- `src/mvdmio.Database.PgSQL.Analyzers/GeneratedAssemblyRegistrationSourceBuilder.cs` — the emitted `AddDatabase(services)` call (read only; must still compile)
- `test/mvdmio.Database.PgSQL.Tests.Integration/ConnectionPoolTests.cs` — DI tests (the file step 01 created), or a sibling class
- `test/mvdmio.Database.PgSQL.Tests.Integration/mvdmio.Database.PgSQL.Tests.Integration.csproj` — `Microsoft.Extensions.DependencyInjection` package reference
- `test/mvdmio.Database.PgSQL.Tests.Unit/ServiceCollectionExtensionsTests.cs` — registration-shape tests for the new overload
- `.agents/refs/dependencies.md` — Tests table row for the added package
- `src/mvdmio.Database.PgSQL/README.md` — the two "Dependency Injection" sections

## Acceptance criteria

- [ ] Integration: `AddDatabase(settings)` followed by `AddDatabase()` gives a resolved `DatabaseConnectionFactory` whose connections show the settings' name in `pg_stat_activity.application_name`. Its pool is exhausted at the settings' cap. Use a short `Timeout` keyword and a small cap.
- [ ] Integration: `AddDatabase()` followed by `AddDatabase(settings)` gives the same result.
- [ ] Two `AddDatabase(settings)` calls with different settings: the later call's settings apply.
- [ ] After any mix of calls, the collection holds exactly one `DatabaseConnectionFactory` registration, and it is a singleton.
- [ ] `AddDatabase(null!)` throws `ArgumentNullException`.
- [ ] The existing `ServiceCollectionExtensionsTests` still pass. The plain overload still registers by type through `TryAdd`.
- [ ] The package README's two "Dependency Injection" sections cover the overload, the order rule and the last-call-wins rule.
- [ ] Whole solution: `dotnet format --verify-no-changes` exits zero, `dotnet build` succeeds, and `dotnet test` passes, run one after another.

## Outcome

Safety fact: `AddDatabase(settings)` leaves exactly one singleton `DatabaseConnectionFactory` registration, built from the last settings passed, whether `AddDatabase()` (and so the generated `AddXxx()`) ran before or after it — so the resolved factory's connections show the settings' name in `pg_stat_activity.application_name` and its pool is exhausted at the settings' cap; if false, an app's DI-registered cap and name are silently dropped by `TryAdd` order and its pools fall back to the defaults (rung 3)
Proof: `dotnet test test/mvdmio.Database.PgSQL.Tests.Integration/mvdmio.Database.PgSQL.Tests.Integration.csproj --filter "FullyQualifiedName~ConnectionPoolTests"` exit 0 — Passed: 13, Failed: 0 (`FactoryRegisteredWithSettings{Before,After}ThePlainOverload_UsesTheSettings`, `FactoryRegisteredWithSettingsTwice_UsesTheLastSettings`); registration shape: `DOTNET_ROLL_FORWARD=Major dotnet test test/mvdmio.Database.PgSQL.Tests.Unit/mvdmio.Database.PgSQL.Tests.Unit.csproj --filter "FullyQualifiedName~ServiceCollectionExtensionsTests|FullyQualifiedName~DatabaseConnectionFactoryTests"` exit 0 — Passed: 19, Failed: 0. The Proof folder refuses writes from this worktree-isolated session, so the Proof names the commands.
Merge risk: easy — reverting the commit removes the new public overload; nothing is persisted and no release is published until the run lands; affects consumers that adopt `AddDatabase(settings)`

Notes:
- The overload removes every `DatabaseConnectionFactory` registration (`RemoveAll`) and adds a singleton delegate `_ => new DatabaseConnectionFactory(settings)`; the settings type itself is not registered, so `AddDatabase()`'s by-type registration still picks the parameterless constructor.
- The generated `ServiceCollectionExtensions.AddDatabase(services)` call stays unambiguous (different arity); the whole solution builds with 0 errors.
- Unit tests check the registration shape by invoking the descriptor's `ImplementationFactory` and reading `Application Name` off the built data source's connection string, so the Unit project needs no `Microsoft.Extensions.DependencyInjection` reference; only the Integration project got it (10.0.3), with a row in `.agents/refs/dependencies.md`.
- The XML doc names `AddDatabase()` in `<c>` rather than a `cref`: a cref to a C# 14 extension member does not resolve (CS1574).
