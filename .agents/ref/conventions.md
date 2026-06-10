# Coding Conventions

A comprehensive `.editorconfig` governs formatting. Run `dotnet format` before building. Key points below.

## General

- Prefer small diffs over broad refactors.
- Avoid speculative abstractions — add them when a second caller actually appears.
- This library ships to NuGet, so treat the public API as a contract: preserve its shape unless the change is an intentional, called-out break (a major version bump).
- Keep files under roughly 500 lines (test files may exceed this). **Do not split a class into `partial` files to dodge the limit — refactor properly instead.**

## Formatting

- **Indentation:** 3 spaces for C# files (not 4, not tabs).
- **Line endings:** CRLF; final newline required.
- **Namespaces:** file-scoped (`namespace Foo;`), following folder structure.

## Naming

- **Public types / methods / properties:** PascalCase.
- **Private fields:** `_camelCase`.
- **Locals / parameters:** camelCase.
- **Constants:** UPPER_SNAKE_CASE.
- **Interfaces:** `I`-prefixed (e.g. `IDbMigration`, `IMigrationRetriever`).
- **Generic parameters:** `T`-prefixed (e.g. `TEntity`).
- **Async methods:** `Async` suffix.

## Code style

- **Nullable reference types:** enabled — respect nullability annotations.
- **Implicit usings:** enabled in all projects.
- **`LangVersion`:** `latest`. **`EnforceExtendedAnalyzerRules`** is on for the library.
- Use `var` when the type is apparent; braces for multi-line statements; prefer pattern matching; use `?.` and `??`.
- Keep using directives minimal — remove duplicates and dead imports.

## Visibility

- Public surface is annotated with `[PublicAPI]` (JetBrains.Annotations).
- Internals are exposed to the test projects via `InternalsVisibleTo` (`...Tests.Unit`, `...Tests.Integration`).

## Documentation

- **XML doc comments are required on all public members** (`GenerateDocumentationFile` is on). Include `<summary>`, `<param>`, `<typeparam>`, `<returns>` as applicable.

## Error handling

- Never swallow exceptions silently (the deliberate best-effort swallow around advisory-lock release in `DatabaseMigrator` is the documented exception — match that bar before adding another).
- Quote the real exception text when reporting/logging a failure.
- In tests, assert the exact behavior or message when it matters.

## Targets

- The library and tool multi-target `net8.0;net9.0;net10.0`. Don't use APIs unavailable on net8.0 without a guard/polyfill (PolySharp is referenced).
