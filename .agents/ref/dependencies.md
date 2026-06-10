# Dependencies & CI/CD

## Main library (`mvdmio.Database.PgSQL`)

| Package | Purpose |
|---------|---------|
| `Dapper` | Micro-ORM for queries |
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
