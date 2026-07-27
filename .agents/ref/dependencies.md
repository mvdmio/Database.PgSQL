# Dependencies & CI/CD

## Main library (`mvdmio.Database.PgSQL`)

| Package | Purpose |
|---------|---------|
| `Dapper` | Micro-ORM for queries |
| `linq2db` | LINQ provider behind the query surface (`db.Linq`, generated `Query()`) — see ADR 0004 |
| `Npgsql` | PostgreSQL ADO.NET provider |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | DI integration |
| `Portable.System.DateTimeOnly` | `DateOnly`/`TimeOnly` support on older TFMs |
| `PolySharp` | Polyfills for newer language features on older TFMs (private) |
| `JetBrains.Annotations` | Code annotations, incl. `[PublicAPI]` (private) |

The library also references `mvdmio.Database.PgSQL.Analyzers` as an analyzer (netstandard2.0) and ships it inside the NuGet package.

## CLI tool (`mvdmio.Database.PgSQL.Tool`)

| Package | Purpose |
|---------|---------|
| `System.CommandLine` | Command-line parsing |
| `YamlDotNet` | Tool configuration files |

Packed as a `dotnet tool` with command name `db`. References the main library.

## Tests

| Package | Purpose |
|---------|---------|
| `xunit.v3` | Test framework |
| `AwesomeAssertions` | Fluent assertions |
| `Testcontainers.PostgreSql` | PostgreSQL container for integration tests |
| `Microsoft.NET.Test.Sdk` / `xunit.runner.visualstudio` / `coverlet.collector` | Test host, runner, coverage |
| `Microsoft.AspNetCore.OData` | **`Tests.Integration.OData` only** — the query front-end the conformance suite drives. Never referenced by anything packable. Its assembly targets `net8.0` and runs on `net10.0`; the 10.x preview line is breaking (it changes the CLR types behind `Edm.Date` and `Edm.TimeOfDay`) |

That project also needs `<FrameworkReference Include="Microsoft.AspNetCore.App" />`: it uses the plain SDK because it hosts nothing, and the OData package brings only its own OData libraries, so the ASP.NET Core types come from the shared framework.

No mocking framework is used — testability comes from interface seams (e.g. `IMigrationRetriever`, `ISchemaExportClientFactory`) and hand-written fakes/fixtures.

## Versioning

- The package version is centralized in `Directory.Build.props` as `<PgSqlVersion>` (both the library and the tool inherit it). Bump it **there**, not in the individual `.csproj` files.
- Semantic versioning: MAJOR = incompatible API change; MINOR = backward-compatible feature; PATCH = backward-compatible fix.
- `RepositoryUrl` is also set in `Directory.Build.props`.

## CI/CD

- **Pipeline:** `.github/workflows/publish-nuget.yml`, runs on `ubuntu-22.04`.
- **Triggers:** push to `main` touching `src/**`, or manual `workflow_dispatch`.
- **Actions:** restore → build (Release) the library and tool → `dotnet pack` the tool → `nuget push` to NuGet.org.
- **Note:** the publish pipeline does **not** run tests. Run `dotnet test` locally before merging to `main`.
