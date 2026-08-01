# 06 — Take the attribute-argument mechanism away

Status: done

## What to build

The contract half of the refactor. There is one way to declare a **Relation**, and after this step the old way does
not compile — which is the point. A developer upgrading meets a build error rather than a declaration that keeps
working differently, so nobody is half-migrated without knowing it.

`RelationAttribute` loses its constructor parameters and becomes a bare marker the generator accepts and ignores. It
stays available for a developer who wants the intent spelled out on the property, and it now has to tell the truth:
putting it on a property that is not a relation fails the build.

Everything the attribute arguments used to drive goes with them. There is no positional matching of foreign-key
properties against a target's primary key, no arity to check, and no type check the compiler does not already do —
because a **Relation key** is a pair of expressions. **Key order** goes back to meaning only what `CONTEXT.md` says it
means: the parameter order of the generated primary-key lookup.

The key-expression association overloads on the public query mapping builder go too. Generated code stopped reaching
them in step 01, and a public overload nothing emits and nothing needs is surface this library does not keep.

### Diagnostics

New:

| Id | Rule | Severity | Trigger |
| --- | --- | --- | --- |
| `PGSQL0033` | Relation attribute on a non-relation property | Error | `[Relation]` sits on a property whose type is not a relation definition |

Retired, with their ids never reused: `PGSQL0012` (foreign-key property not found) and `PGSQL0013` (foreign-key type
cannot match the primary key), both of which disappear into the compiler; and `PGSQL0019` (foreign-key arity), which
has no fixed arity left to check — what it protected is now the uniqueness warning `PGSQL0031` from step 04.

`AnalyzerReleases.Unshipped.md` gains the new rule's row and a removed-rules entry for each of the three, with titles
verbatim.

### The last fixture batch

The analyzer test project still holds about twenty-five relation declarations in its test sources, and they are the
old form's own tests. Convert them: the ones exercising behaviour that survives move to relation definitions, and the
ones exercising `PGSQL0012`, `PGSQL0013` or `PGSQL0019` go, because the mistakes they described are now either
compiler errors or a different rule already covered. Every surviving test class keeps its companion "a well-formed
declaration reports nothing" and "emitted source compiles" assertions, and the harness stubs must end this step
matching the shipped surface exactly — no key-expression overloads, and a `RelationAttribute` with no constructor
parameters. A stub that drifts from the real type makes analyzer tests pass on a shape that would not compile for a
real consumer.

### Sequencing note

This is the contract step of an expand–migrate–contract sequence, so it can only run after steps 02 to 05 have moved
every fixture in the integration and OData suites. Nothing outside the analyzer test project should still be declaring
a relation the old way when this step starts; if something is, convert it here rather than leaving the old path alive.

### Boundaries

- Leave `README.md`, the library's `README.md`, `docs/adr/` and `Directory.Build.props` alone — step 07 owns the
  documentation and the version bump, and this is the step whose outcome those documents describe.

## Acceptance criteria

- [ ] `RelationAttribute` takes no constructor arguments, and a property carrying it whose type is a relation
      definition still resolves exactly as one without it.
- [ ] `PGSQL0033` fires on `[Relation]` over a property that is not a relation definition.
- [ ] `PGSQL0012`, `PGSQL0013` and `PGSQL0019` no longer exist, their ids are not reused anywhere, and each has a
      removed-rules entry in the release tracking file.
- [ ] The attribute-argument declaration form does not compile.
- [ ] The key-expression association overloads are gone from the public query mapping builder and from the harness
      stubs; the harness stubs otherwise mirror the shipped surface.
- [ ] Every relation declaration in the analyzer test project is stated as a relation definition, and the tests for
      the three retired rules are gone rather than re-pointed.
- [ ] No relation is declared the old way anywhere in the repository.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` are all green (Docker running).

## Outcome

`RelationAttribute` (`src/mvdmio.Database.PgSQL/Attributes/RelationAttribute.cs`) is now a bare marker: no
constructor, no `ForeignKeyPropertyNames`, `[AttributeUsage(AttributeTargets.Property)]` only, with its XML doc
rewritten to describe it as optional and to name `PGSQL0033` as what a misuse fails the build with. Every
`[Relation(nameof(...))]` declaration anywhere now fails to compile with `CS1729` (no matching constructor), which is
the "does not compile" the step asked for.

`PGSQL0033` ("Relation attribute on a non-relation property", Error) is new in
`TableRepositoryDiagnostics.cs`. `PGSQL0033` was not already used elsewhere — confirmed by grep before assigning it —
so no id deviation was needed; the sequence's only gap (0033 was free, between 0032 and 0034) is exactly where it
belongs. `PGSQL0012` (foreign key property not found), `PGSQL0013` (foreign key type mismatch) and `PGSQL0019`
(foreign key arity mismatch) are retired: their descriptors are gone from `TableRepositoryDiagnostics.cs`, replaced
by a comment noting the ids are never reused, and every code path that reported them is gone too (see below).

**The generator.** The relation-property split in `TableDefinitionSymbols.IsRelationProperty` is now entirely
type-driven — the `HasAttribute(RELATION_ATTRIBUTE)` shortcut that used to make the attribute alone sufficient is
gone, so a property is a relation only because its type (or collection element type) derives from
`RelationDefinition<,>`. `TableDefinitionParser.Parse` adds one pass over the column candidates: any of them carrying
`[Relation]` reports `PGSQL0033` and is otherwise left to whatever a plain column earns. `TryGetRelationTarget` /
`IsTargetCandidate` in `TableDefinitionSymbols.cs` dropped the old form's fallback branch (`target = named; return
true;` for a type that isn't a relation definition) — the two `out INamedTypeSymbol? relationDefinition` /
`declaringTypeArgument` parameters are always populated together now, and failure means the declaration is malformed
(`TTarget` resolving to something other than a named type) rather than "not a relation at all." `RelationAttributeOf`
and `GetForeignKeyPropertyNames` are deleted from `TableDefinitionSymbols.cs`, since nothing reads the old attribute's
arguments any more.

`RelationResolver.TryResolve` in `RelationResolver.cs` lost its entire old-form branch — the arity check, the
foreign-key-name lookup, the position-by-position `CanJoin` type check, and the `DescribeNames`/`CanJoin` helpers
that supported it — and now always resolves a relation the way `TryResolveDefinitionForm` used to (that method's body
was inlined into `TryResolve`, since there is only one form left to resolve). `RelationDeclarationModel` in
`TableDefinitionModel.cs` lost `ForeignKeyPropertyNames` and `IsDefinitionForm`; `KeyPairs` is no longer nullable,
since every relation the parser produces states its pairs. `CheckForForgottenConditions`'s `!relation.IsDefinitionForm
||` guard is gone with it, since every relation can carry a condition now. `TableDefinitionParser.TryParseRelationDefinition`'s
call site simplified to match (`relationDefinition!` is always non-null, as the parser's own comment now explains).

**The public query mapping builder.** The two key-expression `Relation` overloads on
`QueryEntityMappingBuilder<TEntity>` (`src/mvdmio.Database.PgSQL/Connectors/Linq/QueryEntityMappingBuilder.cs`) —
the `TThisKey`/`TTargetKey` pair, one for a relation to one row and one for a relation to many — are deleted. Only the
two predicate-based overloads remain, and their doc comments were tightened now that they are the only form (no more
"this is the composite-key one" framing, since there is exactly one shape left). No test in
`test/mvdmio.Database.PgSQL.Tests.Unit/Connectors/Linq/QueryEntityMappingBuilderTests.cs` exercised the removed
overloads (grepped first to confirm), so no test there needed deletion.

**`GeneratorHarness.RUNTIME_STUBS`** (`test/mvdmio.Database.PgSQL.Analyzers.Tests/GeneratorHarness.cs`): the
`RelationAttribute` stub lost its constructor to match the shipped type exactly, and the two key-expression `Relation`
overload stubs on the harness's `QueryEntityMappingBuilder<TEntity>` are gone, matching the real removal.

**The last fixture batch**, across the three analyzer test classes that still held the old form:

- `TableRepositoryGeneratorTests.cs` — `VALID_RELATIONS` (Author/Editor/Books) converted to nested private
  `RelationDefinition<,>` classes; the emitted-registration assertions needed no text change (the predicate form is
  identical either way, per step 01/02). Three tests exercising the retired rules were deleted outright rather than
  re-pointed: `RelationWithAnUnknownForeignKey_ProducesDiagnostic` (`PGSQL0012`),
  `RelationWithAForeignKeyThatCannotMatchThePrimaryKey_ProducesDiagnostic` (`PGSQL0013`), and
  `AHandWrittenRelationCall_ResolvesWithoutTypeArguments` (exercised the now-removed key-expression overload, not a
  retired diagnostic, but the shape it tested no longer exists). The surviving diagnostic tests were converted to the
  new form: `PGSQL0014` (target now a `RelationDefinition<BookTable, Elsewhere>` whose `Elsewhere` isn't a `[Table]`
  class), `PGSQL0015` (relation-typed property assigned `= new()` instead of nullable), `PGSQL0017` (relation-typed
  property with no setter), `PGSQL0018` (relation-typed property also carrying `[Column]`). `PGSQL0016` needed an
  unusual fixture to stay reachable at all — see the deviation below — and gained a comment explaining why. Two new
  tests cover `PGSQL0033` itself: `[Relation]` on a plain `AuthorTable?`-typed property, and `[Relation]` on a
  `HashSet<AuthorRelation>` (an unsupported to-many collection wrapping a genuine relation definition, still not a
  relation because `HashSet` isn't a supported collection type). The `RelationSource` helper (only used by the
  deleted `PGSQL0012`/`PGSQL0013` tests) was removed.
- `TableRepositoryGeneratorCompositeKeyTests.cs` — `COMPOSITE_KEY_TABLES` (`ProjectTable.Tasks` /
  `TaskTable.Project`) converted to nested private `RelationDefinition<,>` classes with two-pair `Keys`. Three tests
  for the retired rules were deleted: the `RelationNamingTheWrongNumberOfForeignKeys_...` theory (`PGSQL0019`, 2
  cases), `RelationWithAForeignKeyThatCannotMatchItsKeyMember_...` (`PGSQL0013`), and
  `RelationNamingSeveralUnknownForeignKeys_ReportsEachOfThem` (`PGSQL0012`). The now-unused `TaskSourceWithRelation`
  helper was removed with them. Every surviving test (registration text, composite-key generation, the
  `Relation_IsNeverRegisteredThroughAKeyExpression` guard) needed no assertion changes.
- `TableRepositoryGeneratorTenancyTests.cs` — no test here targeted a retired rule, so all eight inline
  `[Relation(nameof(...))]` fixtures across `PGSQL0027`'s test methods were converted in place to nested private
  `RelationDefinition<,>` classes (`AccountRelation`, `OwnerRelation`, `CategoryRelation`, `DocumentsRelation`,
  `TenantRelation`, each named after the property it backs). No assertion text changed — the diagnostics, their
  counts, and the emitted registration text are unaffected by which form declared the relation, per `RelationResolver`
  reading resolved pairs rather than declaration syntax.

Every surviving test class keeps its "well-formed declaration reports nothing" and "emitted source compiles"
companion assertions unchanged.

`AnalyzerReleases.Unshipped.md` gained `PGSQL0033`'s row and a new "Removed Rules" section (no prior removal existed
in this repo to follow, so the standard Roslyn `AnalyzerReleases` convention — Rule ID | Category | Severity | Notes,
matching the shipped table's columns — was used) listing `PGSQL0012`, `PGSQL0013` and `PGSQL0019` with their shipped
titles verbatim. Checked every descriptor added or reshaped across steps 02–06 (`PGSQL0027` reshaped, `PGSQL0028`
through `PGSQL0035`) against the file: all have a row with a verbatim-matching title except `PGSQL0027`, which needs
none — its id, category and severity are unchanged from the 0.36 release already in `AnalyzerReleases.Shipped.md`, so
Roslyn's release-tracking analyzer (`RS2000`/`RS2002`/`RS2003`) does not ask for a new entry, and the build carries
zero warnings confirming that.

Verification, run sequentially in the foreground with Docker running:
- `dotnet format` — no changes; `dotnet format --verify-no-changes` exits 0.
- `dotnet build` (whole solution) — 0 warnings, 0 errors.
- `dotnet test`, run per project (`DOTNET_ROLL_FORWARD=LatestMajor` for the net9.0 projects, the same pre-existing
  environment quirk steps 01–05 noted): Analyzers.Tests 160/160 (165 pre-existing, net −5: −3 deleted in
  `TableRepositoryGeneratorTests.cs` + 2 added, −4 deleted in `TableRepositoryGeneratorCompositeKeyTests.cs`),
  Tests.Unit 197/197, Tests.Integration 263/263 (Docker/Testcontainers), Tests.Integration.OData 134/134,
  Tests.Packaging 13/13. All green.

Repo-wide grep for `[Relation(` with arguments after all changes: zero hits under `src/` or `test/` (any `.cs` file);
the only remaining hits are in `src/mvdmio.Database.PgSQL/README.md`, which is step 07's boundary and was left alone.

### Deviations

One, forced by the mechanism itself rather than chosen: reaching `PGSQL0016` ("unsupported relation property type")
under the new, entirely type-driven split turns out to need an unusual fixture. Since `IsRelationProperty` and
`TryGetRelationTarget`'s success now share the same underlying check (`TryGetRelationDefinitionBase`), a property
that the parser treats as a relation at all is now, in every ordinary case, also one `TryGetRelationTarget` resolves
successfully — the two questions used to diverge under the old attribute-driven gate (where `[Relation]` alone put a
property into the relation path regardless of its type) but no longer do. The one surviving way to fail
`TryGetRelationTarget` for a property `IsRelationProperty` already accepted is a relation definition whose `TTarget`
type argument resolves to something that satisfies `where TTarget : class` but is not a named type — an array, since
arrays are reference types. The converted test (`RelationTargetingSomethingThatIsNotANamedType_ProducesDiagnostic`)
declares `RelationDefinition<BookTable, AuthorTable[]>` and pairs `BookTable.BookCount` (`long`) against
`AuthorTable[].LongLength` (`long`) to keep `Keys` itself well-formed. `UnsupportedRelationPropertyType`'s message
text was also updated (`"a relation property must be a table definition or a list, collection or sequence of one"` →
`"...must be a relation definition or a list, collection or sequence of one"`), since a plain table-definition-typed
property is no longer a valid relation property at all under the new mechanism — this is a wording fix, not a
reshape, and no id/category/severity changed. No other deviation from the step file or the spec.
