# 04 — The write path carries the tenant, and cannot move a row between tenants

Status: done

## What to build

`CreateAsync` and `UpdateAsync` keep their single command parameter and gain no parameter of their own. Instead the
tenancy column becomes a `required` property on both generated command types, so a construction site that does not set it
fails to build rather than writing a row under a default value. Existing sites keep their object-initializer style.

That is a deliberate exception to the generator's standing rule — stated today in the remarks on the type-emitting
helper — that `required` and `init` are never mirrored from a Table definition. This one is not mirrored, it is added.
Update that remark so the exception is written down where the rule is.

The generated **data type** does not get it. Dapper materializes that type through a parameterless constructor, which
cannot satisfy `required`.

Where the tenancy column sits **outside** the primary key, the generated `UPDATE` changes shape. Two sets that were
already different in the generator move one column further apart:

- what the statement **assigns**: not a key member, not generated, **not a tenancy column**
- what the statement **addresses the row by**: every key member, **plus every tenancy column not already among them**

The column stays on the update command type regardless, because the `WHERE` clause needs its value. So a generated update
never assigns the tenancy column, which is what stops a row changing tenant through the generated surface, and an update
aimed at another tenant's row matches nothing and throws — exactly as an update against a missing key already does. A
consumer who genuinely needs to move a row writes the SQL by hand; that escape hatch is the design, not a gap.

Where the tenancy column **is** a key member, the update already addresses the row by it and already excludes it from the
assignments, so nothing changes.

One consequence falls out and is the right answer rather than a bug: a table whose only assignable column was the tenancy
column now has nothing left to assign, so it trips the existing "no updatable columns" refusal (`PGSQL0007`) where it used
to generate. The message names the table rather than the tenancy declaration, so it will read as unrelated to the line
that caused it. Pin that behaviour with a test so a later reader finds the explanation instead of rediscovering it.

Cover it at both seams:

- **The generator seam.** Extend `TableRepositoryGeneratorTenancyTests`: the `required` property on both command types and
  its absence from the data type; the `UPDATE` carrying the tenancy column in the `WHERE` and not in the `SET`, only where
  the column is outside the key; the unchanged `UPDATE` where it is inside; the create statement still inserting the
  column like any other; and the table left with nothing to assign producing `PGSQL0007`. Keep asserting that the
  generated output compiles.
- **The integration seam.** Over the tenanted table set from step 02: a create writes the row under the tenant the
  required property carries; an update aimed at another tenant's row changes nothing and throws; and an update of the
  caller's own row leaves the tenancy column as it was.

## Acceptance criteria

- [ ] Every tenancy column is a `required` property on the generated create command type and on the generated update
      command type.
- [ ] No tenancy column is `required` on the generated data type, and that type still materializes through its
      parameterless constructor.
- [ ] `CreateAsync` and `UpdateAsync` still take exactly one parameter each besides the cancellation token, and the create
      statement still inserts the tenancy column like any other non-generated column.
- [ ] Where the tenancy column sits outside the primary key, the generated `UPDATE` addresses the row by every key member
      plus that column, and assigns every non-key, non-generated column except that one.
- [ ] Where every tenancy column is a key member, the generated `UPDATE` is byte-for-byte unchanged.
- [ ] The remark stating that `required` and `init` are never mirrored records the tenancy exception.
- [ ] A table whose only assignable column was its tenancy column reports `PGSQL0007` and generates nothing, covered by a
      test that explains why the message names the table rather than the declaration.
- [ ] A table declaring no tenancy column still emits exactly what it emits today.
- [ ] Integration tests show a create landing under the required tenant, a cross-tenant update changing nothing and
      throwing, and an update of the caller's own row leaving the tenancy column as it was.
- [ ] `dotnet format` → `dotnet build` → `dotnet test`, run sequentially and never in parallel, are all clean.
      Integration tests need Docker running.
- [ ] `README.md`, `src/mvdmio.Database.PgSQL/README.md` and `<PgSqlVersion>` in `Directory.Build.props` are untouched —
      the last step of this spec owns all three.

## Outcome

The write path now carries the tenant and cannot move a row between tenants, in
`src/mvdmio.Database.PgSQL.Analyzers/TableDefinitionParser.cs`, `TableRepositorySqlStatements.cs` and
`TableRepositorySourceBuilder.cs`:

- **Parser (`TableDefinitionParser.cs`).** `mutableUpdateProperties` now excludes `IsTenancy` alongside
  `IsPrimaryKey`/`IsGenerated`, so a tenancy column — whether or not it is a key member — never lands in the update's
  `SET` list. `updateProperties` (the update command type's shape) gained the tenancy columns not already in
  `primaryKeys`, appended right after the key and ahead of the mutable columns, so the command type still carries the
  column the `WHERE` clause needs even though it is no longer assignable.
- **SQL text (`TableRepositorySqlStatements.cs`).** `BuildUpdateSql` now builds its `WHERE` with
  `BuildKeyAndTenancyPredicate` (already added in step 03) instead of the old `BuildKeyPredicate`, which is now unused
  everywhere and was deleted — `BuildKeyAndTenancyPredicate`'s doc comment was widened from "the two members" to name
  the update statement as a third caller. Where every tenancy column is already a key member,
  `BuildKeyAndTenancyPredicate` degenerates to exactly the old key predicate, so the update statement comes out
  byte-for-byte unchanged there.
- **C# emission (`TableRepositorySourceBuilder.cs`).** `AppendDto` gained a `mirrorsTenancyAsRequired` parameter: `true`
  for the create and update command types, `false` for the data type. A tenancy column gets `public required` instead
  of `public` on the two command types only, and skips the null-forgiving `= default!;` initializer when it does
  (`required` already tells the compiler the value will be assigned, so the initializer is redundant rather than
  wrong). The XML doc remark on `AppendDto` — which states `required`/`init` are never mirrored — gained a new
  paragraph naming this as the one deliberate exception, and why the data type is excluded (Dapper materializes it
  through a parameterless constructor `required` cannot satisfy).
- **The consequence the spec calls out.** A table whose only non-key, non-generated column was its tenancy column now
  has nothing left in `mutableUpdateProperties`, so it trips the pre-existing `PGSQL0007` ("no updatable columns")
  refusal where it used to generate — no new diagnostic, the existing one just reaches further. Pinned by
  `TenancyColumn_AsTheOnlyAssignableColumn_ReportsPGSQL0007_AndGeneratesNothing`.

Generator seam: extended `TableRepositoryGeneratorTenancyTests`
(`test/mvdmio.Database.PgSQL.Analyzers.Tests/TableRepositoryGeneratorTenancyTests.cs`, now 24 tests, +5) with a new
`TENANCY_ONLY_ASSIGNABLE_COLUMN` table constant and tests covering: the `required` property on both command types and
its absence from the data type; `CreateAsync` still inserting the tenancy column like any other; the `UPDATE`
carrying the tenancy column in `WHERE` and excluding it from `SET` where it sits outside the key; the `UPDATE`
predicate/assignment list staying unchanged where the tenancy column is already a key member; and the
`PGSQL0007`/nothing-generated case. All prior 19 tests still pass unmodified.

Integration seam: extended `GeneratedRepositoryTenancyTests`
(`test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/GeneratedRepositoryTenancyTests.cs`, +4 tests)
over the existing `TenancyDocumentTable`/`TenancySettingTable` from step 02 — no new tables needed. New tests: a
create writes the row under the tenant the required property carries (and it is invisible to another tenant); an
update aimed at another tenant's row changes nothing and throws, for both tenancy placements; and an update of the
caller's own row succeeds and leaves the tenancy column as it was. The throw is asserted as
`mvdmio.Database.PgSQL.Exceptions.QueryException` (the library's wrapper around Dapper's zero-row
`InvalidOperationException` from `QuerySingleAsync`) rather than the raw `InvalidOperationException` — the same
wrapping every other "update against a missing key" already goes through, confirmed by running the test against real
output rather than assuming the unwrapped type.

`dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` (sequential, `DOTNET_ROLL_FORWARD=LatestMajor`
for the net9.0 test hosts, Docker running) all pass: Unit 197/197, Analyzers.Tests 115/115 (110 prior + 5 new),
Integration.OData 134/134, Packaging 13/13, Integration 256/256 (252 prior + 4 new). `README.md`,
`src/mvdmio.Database.PgSQL/README.md` and `Directory.Build.props` are untouched, reserved for the spec's last step.

No deviations from the step or spec.
