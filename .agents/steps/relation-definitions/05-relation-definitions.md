# 05 — Move the OData suite onto the new declaration form

Status: done

## What to build

The OData conformance suite is the standing check that a **Query front-end** sees the same **Query surface** it always
did. Convert its fixtures — the author, book, tenant project and tenant task tables, six relation declarations between
them — from the attribute-argument form to a **Relation definition** per relation, and change nothing else.

No new tests. The point of this step is that every conformance and regression test passes unchanged: the same rows,
the same SQL shape, the same `$expand` behaviour, the same navigation paths and collection quantifiers, the same
misconfiguration regressions. If a result moves, the Query surface's behaviour moved with it and that is a defect in
the earlier steps rather than something to re-pin here.

The EDM model is built from the generated data types, and those are unchanged in shape by this feature — a relation
still appears there as the target's generated data type, or a list of it, never as the relation definition class. So
the model-shape tests over relation properties should need no edit either.

This is the last of the fixture batches. After it, only the analyzer test project still declares relations the old
way, and step 06 converts those as it removes the old form.

### Boundaries

- Fixture declarations only. Do not touch the conformance tests, the regression tests, the OData configuration models
  or the suite's own `README.md`.
- No diagnostic changes, so `AnalyzerReleases.Unshipped.md` needs nothing. Leave `README.md`, the library's
  `README.md`, `docs/adr/` and `Directory.Build.props` alone — step 07 owns them.
- The old attribute-argument form must still resolve at the end of this step; step 06 removes it.

## Acceptance criteria

- [ ] All six relation declarations in the OData suite's fixtures are stated as relation definitions, with their key
      pairs as expressions.
- [ ] No conformance test, regression test, model-shape test or OData configuration file is edited.
- [ ] The whole OData suite passes unchanged against its container, including the `$expand` cases, the navigation-path
      and collection-quantifier cases, the composite-key cases and the three misconfiguration-regression classes.
- [ ] The generator reports no new warning over the OData fixtures — in particular no uniqueness or cross-tenant
      warning that the conversion introduced by pairing something differently than the old declaration did.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` are all green (Docker running).

## Outcome

All six relation declarations in `test/mvdmio.Database.PgSQL.Tests.Integration.OData/Fixture/` now use the
`RelationDefinition<,>` class form, converted from the attribute-argument form the same way the main integration
suite's `AuthorTable`/`BookTable`/`TenantProjectTable`/`TenantTaskTable` fixtures already had been (used as the
pattern to follow):

- `AuthorTable`: `MentorRelation` (`MentorId` → `AuthorId`), `MenteesRelation` (`AuthorId` → `MentorId`), `BooksRelation`
  (`AuthorId` → `BookTable.AuthorId`).
- `BookTable`: `AuthorRelation` (`AuthorId` → `AuthorTable.AuthorId`).
- `TenantProjectTable`: `TasksRelation` (`(AccountId, ProjectId)` → `(AccountId, ProjectId)` on `TenantTaskTable`).
- `TenantTaskTable`: `ProjectRelation` (`(AccountId, ProjectId)` → `(AccountId, ProjectId)` on `TenantProjectTable`).

Each relation property (`Mentor`, `Mentees`, `Books`, `Author`, `Tasks`, `Project`) became `private`, backed by a
`private` nested `RelationDefinition<,>` class, matching the already-converted main-suite fixtures. This is safe here
because nothing in the OData suite's tests, conformance checks, or `ODataConfiguration` ever touches these
Table-definition properties directly — every assertion and every EDM/query-option reference goes through the
generated data types (`AuthorData`, `BookData`, `TenantProjectData`, `TenantTaskData`) and their navigation properties,
confirmed by grepping the whole test project for `.Mentor`/`.Mentees`/`.Books`/`.Author`/`.Tasks`/`.Project` usage
before making the change.

No conformance test, regression test, model-shape test, or OData configuration file was touched — only the four
fixture files under `Fixture/`. The old attribute-argument form still compiles elsewhere (untouched by this step, per
the boundary).

Verification: `dotnet format --verify-no-changes` exits 0. `dotnet build` is clean with 0 warnings, 0 errors — in
particular no new PGSQL0027 (tenancy) or PGSQL0031 (uniqueness) warning was reported for the converted fixtures, so no
deviation to report there. `dotnet test` is green across every project: the OData suite (134/134, including all
`$expand`, navigation-path, collection-quantifier, composite-key and misconfiguration-regression tests, unchanged),
the analyzer tests (165/165, `DOTNET_ROLL_FORWARD=Major` needed to launch the net9.0 host on this machine's net10.0-only
SDK — a pre-existing environment quirk, not a code issue), the unit tests (197/197), the packaging tests (13/13), and
the main integration suite (263/263, Docker running).

No deviations from the spec or from earlier steps' carried context.
