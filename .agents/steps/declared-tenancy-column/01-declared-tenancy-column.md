# 01 — Make room in the repository source builder

Status: done

## What to build

Nothing a consumer can see. `TableRepositorySourceBuilder` is the file every remaining step adds to — new
parameter lists, a narrowed `Query`, a rewritten `UPDATE` predicate — and it already sits at 452 lines against the
project's roughly-500-line ceiling. `conventions.md` forbids splitting a class into `partial` files to dodge that limit,
so make the change easy first: separate the SQL text the builder composes from the C# it emits, leaving one type
responsible for statements and one for source.

The seam is already visible in the file. Everything from `BuildCreateSql` down through `QuoteIdentifier` — the create,
get-all, get-by, get-by-key, update, delete-by and delete-by-key statements, the key predicate, the select and returning
lists, the qualified table name and the identifier quoting — is text about PostgreSQL and touches nothing about C#. The
emission half keeps `AppendSqlLiteral`, the parameter dictionary, the bindings and the member appenders.

This is a pure prefactor. The emitted source must come out byte-for-byte as it does today, which is exactly why no test
file may be edited: the analyzer tests assert on exact emitted strings, so they are the proof.

## Acceptance criteria

- [ ] The statement-building helpers live in their own `internal` type in `src/mvdmio.Database.PgSQL.Analyzers/`, and
      `TableRepositorySourceBuilder` calls into it rather than owning them.
- [ ] Neither type is split across `partial` declarations, and both files are comfortably under 500 lines.
- [ ] No test file is edited, added or removed. Every existing analyzer test passes as written, which is what pins the
      emitted source as unchanged.
- [ ] `dotnet format` → `dotnet build` → `dotnet test`, run sequentially and never in parallel, are all clean.
      Integration tests need Docker running.
- [ ] `README.md`, `src/mvdmio.Database.PgSQL/README.md` and `<PgSqlVersion>` in `Directory.Build.props` are untouched —
      the last step of this spec owns all three, so the documentation describes the finished surface once.

## Outcome

Extracted every SQL-text helper — `BuildCreateSql`, `BuildGetAllSql`, `BuildGetBySql`, `BuildGetByPrimaryKeySql`,
`BuildUpdateSql`, `BuildDeleteBySql`, `BuildDeleteByPrimaryKeySql`, `BuildKeyPredicate`, `BuildSelectList`,
`BuildReturningList`, `FullyQualifiedTableName` and `QuoteIdentifier` — into a new internal static type,
`src/mvdmio.Database.PgSQL.Analyzers/TableRepositorySqlStatements.cs` (90 lines). `TableRepositorySourceBuilder.cs`
now calls into it (`TableRepositorySqlStatements.BuildCreateSql(model)` etc.) instead of owning the SQL text, and is
down to 375 lines. Neither type is `partial`. No test file was touched.

`dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` (run sequentially, `DOTNET_ROLL_FORWARD=LatestMajor`
for the net9.0 test hosts, Docker running for integration tests) all pass: Unit 191/191, Analyzers.Tests 89/89
(unchanged count and all green — the byte-for-byte emission proof), Integration.OData 134/134, Packaging 13/13,
Integration 239/239. `README.md`, `src/mvdmio.Database.PgSQL/README.md` and `Directory.Build.props` are untouched.

No deviations from the step or spec.
