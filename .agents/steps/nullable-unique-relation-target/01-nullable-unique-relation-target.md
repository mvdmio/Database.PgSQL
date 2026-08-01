# 01 — Move PGSQL0035 from the nullable unique target to the pair of two nullable columns

Status: done

## What to build

The rule behind `PGSQL0035` stops asking whether a **Relation key**'s target column is `[Unique]` and nullable, and
starts asking whether *both* columns of the pair can hold null.

What a developer observes after this step:

- A not-null foreign key paired against a target column marked `[Unique]` that can hold null builds silently. The
  **Relation** resolves, the association reaches the assembly registration, and the target appears on the generated data
  type — everything an ordinary relation generates. No `PGSQL0035`, no `PGSQL0031` either: a nullable `[Unique]` column
  still satisfies the uniqueness claim, so a relation to one row against it warns about nothing.
- A pair whose two columns can both hold null fails the build with `PGSQL0035`, wherever it appears — whether or not
  either column is `[Unique]`. That second half is the case that is silently accepted today.
- The failure names the fix: claim one side cannot hold null, or pair a column that cannot.
- `[Column(NotNull = true)]` on either side clears the failure, including in a file where nullable annotations are
  switched off, because the rule reads the **Nullability claim** the mapping registers rather than the property's C#
  type. Those two notions look interchangeable and are not — `PropertyDefinitionModel.IsDeclaredNotNull` is the claim,
  `IsNullable` is the C# type. Reading the wrong one is exactly how the current rule came to check something the query
  provider never sees.
- A relation with two offending pairs reports twice — each pair is a separate mistake.
- A refused relation takes only itself down: the rest of the table still generates, and the relation reports nothing
  further about target uniqueness or tenancy pairing, both of which are moot once it is gone.
- A **Relation condition** that excludes nulls does not rescue a refused pair. The pair is refused whatever the
  condition says.

Severity, blast radius and the id are all unchanged: still an error, still drops only the relation. `PGSQL0035` keeps
its id with a new title, message and description, because nothing in production depends on its current meaning.

The primary-key nullability rule (`PGSQL0020`) is untouched and keeps reading the property's C# type — a key member's
type is the fact there, rather than a claim about it.

Nothing about `RelationDefinition<,>` or `Key(...)` changes. The one same-type overload already infers its type argument
from both lambdas at once and so accepts every nullability combination, including a `long` paired against a `long?`.
Prove that with a test rather than assuming it: the analyzer must be the thing that reports the bad pair, not the
compiler.

## Footprint

Projects: `src/mvdmio.Database.PgSQL.Analyzers`, `test/mvdmio.Database.PgSQL.Analyzers.Tests`. The generator runs over
every project that declares a **Table definition**, so `test/mvdmio.Database.PgSQL.Tests.Integration`,
`test/mvdmio.Database.PgSQL.Tests.Integration.OData`, `test/mvdmio.Database.PgSQL.Tests.Integration.SecondarySchema` and
`test/mvdmio.Database.PgSQL.Tests.Packaging` all compile through the changed rule and must stay green — no existing
fixture pairs two nullable columns, so nothing there is expected to move.

- `src/mvdmio.Database.PgSQL.Analyzers/RelationResolver.cs` — `CheckKeyPairClaims` (the `pair.TargetKey.IsUnique &&
  pair.TargetKey.IsNullable` test and the summary comment above it), `PairedColumnsClaimUniqueness`
- `src/mvdmio.Database.PgSQL.Analyzers/TableRepositoryDiagnostics.cs` — `RelationKeyPairsAgainstNullableUniqueColumn`
  (`PGSQL0035`: id, title, `messageFormat`, `description`; the descriptor name no longer describes the rule)
- `src/mvdmio.Database.PgSQL.Analyzers/TableDefinitionModel.cs` — `PropertyDefinitionModel.IsDeclaredNotNull` versus
  `IsNullable`, and the doc comments that distinguish them
- `src/mvdmio.Database.PgSQL.Analyzers/NullabilityClaim.cs` — where the claim is settled, for reference
- `src/mvdmio.Database.PgSQL.Analyzers/ResolvedRelationModel.cs` — `RelationCandidate`, `JoinedKeyPair.ThisKey`,
  `JoinedKeyPair.TargetKey`
- `src/mvdmio.Database.PgSQL.Analyzers/AnalyzerReleases.Shipped.md` — the `PGSQL0035` note under `## Release 0.37`,
  which describes the retired meaning
- `test/mvdmio.Database.PgSQL.Analyzers.Tests/TableRepositoryGeneratorRelationKeyClaimsTests.cs` — the class summary,
  `RelationPairedAgainstANullableUniqueColumn_ReportsPGSQL0035_AndDropsOnlyThatRelation` (inverts: the relation must now
  be *present* in the registration), `RelationPairedAgainstANonNullableUniqueColumn_ReportsNoPGSQL0035` (unchanged),
  `EveryKeyClaimsShape_EmitsSourceThatCompiles`
- `test/mvdmio.Database.PgSQL.Analyzers.Tests/GeneratorHarness.cs` — `RunGenerator`'s `nullableContextOptions`
  parameter, for the nullable-oblivious case; `RegistrationSource`, `AssertGeneratedSourcesCompile`

## Acceptance criteria

- [ ] A not-null foreign key paired against a nullable `[Unique]` target column reports no diagnostic and the relation
      reaches the assembly registration
- [ ] The existing non-nullable-target case is unchanged and still silent
- [ ] A pair whose two columns can both hold null reports `PGSQL0035` as an error, drops only that relation, and the
      rest of the table still generates
- [ ] The same pair against a target column that is *not* `[Unique]` reports `PGSQL0035` — the case that is silent today
- [ ] `[Column(NotNull = true)]` on one side of that pair clears the report
- [ ] In a compilation with nullable annotations switched off, an unannotated `string` on both sides reports
      `PGSQL0035`, and `[Column(NotNull = true)]` on one side clears it
- [ ] A relation with two offending pairs reports `PGSQL0035` twice
- [ ] A refused pair carrying a **Relation condition** that excludes nulls still reports `PGSQL0035`
- [ ] A relation to one row against a nullable `[Unique]` column reports no `PGSQL0031`
- [ ] A refused relation reports nothing further about target uniqueness or tenancy pairing
- [ ] Every nullability combination of a value-typed pair (`long`/`long`, `long?`/`long`, `long`/`long?`,
      `long?`/`long?`) compiles against the shipped `Key(...)` signatures, so the analyzer is what reports the bad one
- [ ] The `PGSQL0035` message names the fix — claim one side cannot hold null, or pair a column that cannot
- [ ] `PGSQL0035` keeps its id and its `Error` severity
- [ ] `dotnet format --verify-no-changes` exits zero, `dotnet build` is clean, and `dotnet test` is green across the
      solution

## Outcome

`RelationResolver.CheckKeyPairClaims` now refuses a **Relation key** pair when both `pair.ThisKey.IsDeclaredNotNull`
and `pair.TargetKey.IsDeclaredNotNull` are false — i.e. when neither side's registered **Nullability claim** rules out
null — rather than when the target side is `[Unique]` and its C#-type-level `IsNullable` is true. Uniqueness plays no
part in the check any more; `PairedColumnsClaimUniqueness` (used by `PGSQL0031`) is untouched and still treats a
nullable `[Unique]` column as satisfying the uniqueness claim, so a relation to one row against it still warns about
nothing. `PropertyDefinitionModel.IsDeclaredNotNull` already existed with exactly the semantics the spec calls for
(the claim, not the type), so no new model or symbol-reading code was needed — only the resolver's check and the
diagnostic it reports changed.

`TableRepositoryDiagnostics.RelationKeyPairsAgainstNullableUniqueColumn` is renamed to `RelationKeyPairBothNullable`
(the field name — `PGSQL0035`'s id, category and `Error` severity are unchanged). Title, `messageFormat` and
`description` were rewritten to state the new shape and name the fix (claim one side `[Column(NotNull = true)]`, or
pair a column that cannot hold null); the message now also names the declaring-side property, not just the target
side, since either side can be the one at fault. `AnalyzerReleases.Shipped.md`'s `PGSQL0035` note under Release 0.37
was updated to the new title text in place, since the id/category/severity triple it tracks did not change.

Tests in `TableRepositoryGeneratorRelationKeyClaimsTests.cs`: the old "nullable unique target is refused" test is
replaced by `RelationPairedAgainstANullableUniqueColumn_ReportsNoPGSQL0035_AndRegistersTheRelation`, built with a
not-null `string` foreign key (the old fixture had it as `string?`, which would itself now be a both-nullable pair and
trip `PGSQL0035` — worth flagging for later steps that reuse this fixture shape). New tests cover: a pair of two
nullable columns against a non-`[Unique]` target (the case silent before), `[Column(NotNull = true)]` clearing the
report, the nullable-oblivious-file case with an unannotated `string` (built via `NullableContextOptions.Disable`,
already a `GeneratorHarness.RunGenerator` parameter) and its `[Column(NotNull = true)]` fix, two offending pairs on one
relation reporting `PGSQL0035` twice, and a Relation condition excluding nulls not rescuing a refused pair. The
"both nullable" fixtures use `[Column(Null = true)]` on a plain (non-`?`) `string` rather than an actual `string?`
type, because `[Column(NotNull = true)]` on a genuinely nullable-typed property (`string?` or `long?`) always
contradicts the type (`PGSQL0021`) and falls back to the type's own nullable answer — it cannot be used to
demonstrate the claim overriding the type outside the nullable-oblivious case. `EveryKeyClaimsShape_EmitsSourceThatCompiles`
was left as-is and a new `EveryValueTypeNullabilityCombination_BindsAgainstTheOneKeyOverload_AndCompiles` test added,
compiling all four `long`/`long?` combinations of the one same-type `Key(...)` overload against `AssertGeneratedSourcesCompile`
(which discards generator diagnostics, so a combination the analyzer refuses is simply a dropped relation, not a
compile failure).

No production code outside the analyzer package changed. `dotnet format --verify-no-changes`, `dotnet build` and
`dotnet test` are all green across the whole solution (776 tests passed, 0 failed, across
`mvdmio.Database.PgSQL.Tests.Unit`, `.Integration`, `.Integration.OData`, `.Analyzers.Tests` and `.Tests.Packaging`;
`.Tests.Integration.SecondarySchema` is a schema-fixture project with no test methods of its own and only needed to
compile, which it does).

Deviations from the footprint: none of substance. `AnalyzerReleases.Unshipped.md` was checked and is empty, so nothing
there needed touching. The `Directory.Build.props` version bump, README/CONTEXT.md/ADR updates named in the spec's
Implementation Decisions are out of this step's footprint and were left for a later step.
