# Coding Conventions

A comprehensive `.editorconfig` governs formatting. Run `dotnet format` before building. Key points below.

## General

- The `db` tool calls the library's internal logic (it sees internals through `InternalsVisibleTo`) instead of restating a library rule. The tool's own copy of the pending-migration rule drifted from the migrator's and hid out-of-order migrations.
- This library ships to NuGet, so treat the public API as a contract: preserve its shape unless the change is an intentional, called-out break (a breaking version bump).
- Register a type that has more than one public constructor through a factory delegate (`_ => new Foo()`), not by type. The DI container builds it with the longest constructor it can fill, so registering a parameter's type elsewhere silently changes what gets built. The plain `AddDatabase()` registered `DatabaseConnectionFactory` by type and would have picked up any `DatabaseConnectionFactorySettings` in the container.
- **Do not split a class into `partial` files to dodge the ~500-line limit — refactor properly instead.**

## Formatting

- `.gitattributes` normalizes every tracked file to LF on checkout, so the working tree matches `.editorconfig` on any OS.

## Code style

- **Implicit usings:** enabled in all projects.
- **`LangVersion`:** `latest`. **`EnforceExtendedAnalyzerRules`** is on for the library.

## Comments

- Code carries no comments ([ADR 0013](../../docs/adr/0013-code-carries-no-comments.md)). Explain the code through names and structure. A reason a name cannot carry goes into an ADR. The build fails on a comment with `MVDM0053`.
- A `///` doc comment stays only on a public or protected member of the library, because its users read it in IntelliSense. A `///` anywhere else fails, the Tool, the analyzer and the test projects included.
- A justified `#pragma warning disable` carries its reason at the end of the pragma line, never on the line above.

## Visibility

- Public surface is annotated with `[PublicAPI]` (JetBrains.Annotations).
- Internals are exposed to the test projects via `InternalsVisibleTo` (`...Tests.Unit`, `...Tests.Integration`, `...Tests.Integration.OData`). One item group in `mvdmio.Database.PgSQL.csproj` owns the whole list — add there, not in a second group.

## Error handling

- The deliberate best-effort swallow around advisory-lock release in `DatabaseMigrator` is the one documented silent catch — match that bar before adding another.
- In tests, assert the exact behavior or message when it matters.

## Targets

- The library and tool multi-target `net10.0;net11.0`. Don't use APIs unavailable on net10.0 without a guard/polyfill (PolySharp is referenced).
