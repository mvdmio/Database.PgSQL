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

- **Pipeline:** `.github/workflows/publish-nuget.yml`, every job on `ubuntu-latest` with a `timeout-minutes`. The pipeline exists to **publish**; the format check and the tests are the gate on that act, which is why it is not widened to every change.
- **Triggers:** push to `main` touching `src/**` or `Directory.Build.props` (`PgSqlVersion` lives there, so a version bump alone can trigger a re-release), or manual `workflow_dispatch`. A change touching only test projects triggers nothing — it is covered by the next run that publishes.
- **Concurrency:** one `publish-nuget` group with `cancel-in-progress: false`, so runs serialize and an in-flight upload is never cancelled by a newer push.
- **Three jobs, each gating the next:**
  - `build` — `dotnet format --verify-no-changes` first (cheapest failure), then restore and build the solution in Release, then `dotnet pack` the tool with `--no-build`. This is the only compile. Uploads two artifacts: the build output (`bin` **and** `obj` — `--no-build` still evaluates each project, and evaluation reads the generated files under `obj`) and the packages.
  - `test` — downloads the build output, runs `dotnet restore` (needed even with nothing to compile: the test SDK packages contribute `.props`/`.targets` imported from the NuGet cache), then the unit and analyzer suites, then the two container-backed integration suites. Cheap suites first so a logic-level break reports before any container image is pulled. GitHub-hosted `ubuntu` runners ship Docker; macOS and Windows runners do not, which is one reason the pipeline stays on Linux.
  - `publish` — downloads the packages artifact (no checkout) and pushes with `dotnet nuget push --skip-duplicate`, so a re-run against an unchanged `PgSqlVersion` is a harmless no-op. Pushing a `.nupkg` uploads its sibling `.snupkg`.
- **No `nuget.exe`:** the upload uses `dotnet nuget push`. The old workflow pinned `ubuntu-22.04` only because `nuget.exe` runs under Mono, which is absent on Ubuntu 24.04 ([NuGet/setup-nuget#168](https://github.com/NuGet/setup-nuget/issues/168)); dropping the Mono dependency is what lets every job run on `ubuntu-latest`.
- **No pull-request build** and **no dependency caching** — work lands by direct push to `main`, and there are no `packages.lock.json` files for `setup-dotnet`'s cache to key on.
- **Escape hatch:** put `[skip ci]` in the commit message to bypass the pipeline on a push that touches a triggering path but ships nothing. GitHub also honours `[ci skip]`, `[no ci]`, `[skip actions]`, `[actions skip]` and a `skip-checks: true` trailer — no other spelling works.
