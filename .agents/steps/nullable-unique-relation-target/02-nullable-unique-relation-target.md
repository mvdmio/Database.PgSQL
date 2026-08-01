# 02 — A Relation against a nullable unique column, end to end against PostgreSQL

Status: done

## What to build

The shape step 01 admits, demonstrated against a real database: a **Relation** whose **Relation key** pairs a not-null
foreign key on the declaring side against a target column marked `[Unique]` that can hold null.

The integration fixture gains the shape. A target table gets a nullable `[Unique]` column with a real `UNIQUE`
constraint in its DDL — PostgreSQL admits any number of nulls under one, which is what makes the fixture honest about
what is being tested — and a declaring table gets a not-null column of the same type plus the **Relation definition**
pairing the two. The fixture's table definitions are compiled by the real generator, so a fixture that declares this
relation at all is itself the proof that the pairing compiles with no change to `Key(...)`.

What is then observable through the generated repository:

- Filtering across the relation renders a plain cross-table equality and no `IS NULL`. The widened alternative is
  exactly what would cost the unique index behind the column, which is the concern ADR 0006 measured.
- The relation with nothing filtering the far side renders an outer join, so a row whose foreign key matches no target
  row survives the query with nothing attached.
- Materializing the relation reaches the related row; a target row whose unique column is null is reached by nothing,
  because equality never matches null.

Assert in the style the composite-key class already uses for this exact concern: a cross-table equality per key column,
`NotContain("IS NULL")` with the reason stated in the assertion, and SQL read through the existing query diagnostics
helper. No test asserts a performance number — ADR 0006 measured the widening once and the record stands; what these
pin is the SQL shape the measurement was about.

`AuthorTable`/`BookTable` in the generated-repository fixture are the natural home: `AuthorTable` gains the nullable
`[Unique]` column and `BookTable` the not-null counterpart plus the relation. If existing assertions on those two
tables make that churn, a dedicated pair of tables is an equally good answer — the requirement is the shape, not the
table.

## Footprint

Projects: `test/mvdmio.Database.PgSQL.Tests.Integration` (Docker required — Testcontainers). The changed table
definitions compile through the generator, so `src/mvdmio.Database.PgSQL.Analyzers` and
`src/mvdmio.Database.PgSQL` are in the loop and the whole suite must stay green.

- `test/mvdmio.Database.PgSQL.Tests.Integration/Fixture/TestFixture.cs` — the committed `CREATE TABLE` for
  `public.generated_authors` and `public.generated_books`, where the nullable unique column and its `UNIQUE` constraint
  land
- `test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/AuthorTable.cs` — the target side; the new
  nullable `[Unique]` column
- `test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/BookTable.cs` — the declaring side; the new
  not-null column and its `RelationDefinition<BookTable, AuthorTable>`
- `test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/GeneratedRepositoryRelationTests.cs` — where the
  new cases live, or a sibling class if that file has outgrown itself
- `test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/GeneratedRepositoryCompositeKeyTests.cs` — prior
  art only: `Query_ReachingACompositeRelation_ConstrainsEveryKeyColumnWithPlainEquality`,
  `Query_ReachingACompositeRelationWithoutFilteringIt_RendersAnOuterJoin`, `CrossTableEquality`, `QualifiedColumn`,
  `RenderSql`
- `test/mvdmio.Database.PgSQL.Tests.Integration/Fixture/TestBase.cs` — `Db`, `CancellationToken`, the per-test
  transaction rollback

## Acceptance criteria

- [ ] The fixture creates a target table with a nullable column carrying a real `UNIQUE` constraint, and a declaring
      table with a not-null column of the same type
- [ ] The declaring **Table definition** declares the **Relation** pairing those two columns, and the solution builds —
      no `PGSQL0035`, no `PGSQL0031`, no change to `Key(...)`
- [ ] Filtering across the relation renders a plain cross-table equality on the paired columns and contains no
      `IS NULL`, with the reason stated in the assertion
- [ ] The relation with nothing filtering the far side renders an outer join
- [ ] Materializing the relation reaches the related row
- [ ] A target row whose unique column is null is reached by nothing through the relation
- [ ] A declaring row whose foreign key matches no target row is still returned, with nothing attached
- [ ] No test asserts a performance number
- [ ] `dotnet format --verify-no-changes` exits zero, `dotnet build` is clean, and `dotnet test` is green across the
      solution

## Outcome

Used a dedicated table pair rather than growing `AuthorTable`/`BookTable`. `BookTable.AuthorId`/`EditorId` are
nullable, and every existing `CreateBookCommand` call site across the relation and OData test suites omits them; adding
a *not-null* column to `BookTable` would have forced the generated `CreateBookCommand` to require it everywhere those
suites construct a book, which is exactly the churn the footprint's escape hatch anticipated ("a dedicated pair of
tables is an equally good answer — the requirement is the shape, not the table"). A new pair keeps that churn at zero
and keeps the concern isolated:

- `test/mvdmio.Database.PgSQL.Tests.Integration/Fixture/TestFixture.cs` gained `public.generated_catalog_entries`
  (target: `entry_id` PK, `sku TEXT NULL UNIQUE` — a real `UNIQUE` constraint over a nullable column) and
  `public.generated_catalog_items` (declaring: `item_id` PK, `sku TEXT NOT NULL`).
- `test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/CatalogEntryTable.cs` (new) — the target side,
  `[Unique] public string? Sku`.
- `test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/CatalogItemTable.cs` (new) — the declaring side,
  a not-null `Sku` plus `EntryRelation : RelationDefinition<CatalogItemTable, CatalogEntryTable>` keyed
  `Key(x => x.Sku, y => y.Sku)`. This alone is the proof step 01 asked for: the pairing compiles with no change to
  `Key(...)`, no `PGSQL0035`, no `PGSQL0031`.
- `test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/GeneratedRepositoryNullableUniqueRelationTargetTests.cs`
  (new sibling class, following the footprint's other escape hatch) — four tests in the composite-key class's style:
  `Query_ReachingTheNullableUniqueRelationTarget_ConstrainsWithPlainEquality` (cross-table equality regex,
  `NotContain("IS NULL")` with the reason stated), `Query_ReachingTheNullableUniqueRelationTargetWithoutFilteringIt_RendersAnOuterJoin`
  (`LEFT JOIN`), `Query_MaterializingTheRelation_ReachesTheRelatedRowAndLeavesTheUnmatchedRowEmpty` (an item whose sku
  matches an entry reaches it; an item matching no entry stays unattached), and
  `Query_MaterializingTheRelation_NeverReachesTheEntryWhoseUniqueColumnIsNull`.

Drift from the footprint worth flagging for later steps: an initial version of the last test filtered
`x.Entry!.Sku == null` and asserted the result was empty, on the theory that equality can never match a null target.
That is true but the test as written was unsound — an *unmatched* outer join also carries a null `Entry.Sku` (no
row joined at all), so an item with no matching entry (`gizmo-9`) satisfied `Entry!.Sku == null` too, for a reason
that has nothing to do with the target row actually being null. The rewritten test instead materializes every item
with `Include(x => x.Entry)` and asserts none of them carries the specific `EntryId` of the null-`sku` entry — the
only way to observe "unreachable" without conflating it with "reaches nothing because there is no match at all."
Also dropped an `sql.Should().Contain("INNER JOIN")` assertion that `GeneratedRepositoryCompositeKeyTests` uses for
its analogous case: here the provider left the join as `LEFT JOIN` even under an equality filter on the far side
(unlike the composite case, where the filtered column itself is not nullable). Not a contradiction of any acceptance
criterion — nothing requires the join *type* under a far-side filter, only the equality shape and the absence of
`IS NULL` — so the assertion was simply removed rather than pursued further.

`dotnet format --verify-no-changes`, `dotnet build`, and `dotnet test` (via `DOTNET_ROLL_FORWARD=LatestMajor`, this
environment's net9.0 test projects otherwise fail to launch — see the `dotnet-env-quirks` memory) are all green: 780
tests passed, 0 failed, across `mvdmio.Database.PgSQL.Tests.Unit`, `.Integration`, `.Integration.OData`,
`.Analyzers.Tests` and `.Tests.Packaging`. No production code changed; the whole diff is fixture and test-project
files.
