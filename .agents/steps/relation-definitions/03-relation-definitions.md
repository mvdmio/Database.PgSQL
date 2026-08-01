# 03 — A relation carries a condition

Status: done

## What to build

A **Relation definition** may state a **Relation condition**: an ordinary C# expression over the two rows that narrows
the relation beyond its **Relation keys**. It is written where the developer can see it, so the compiler checks it
there, and it is inlined into the join alongside the pairs.

```csharp
public override Expression<Func<LinkTable, PersonTable, bool>> Condition
   => (link, person) => link.TargetKind == TargetKind.Person;
```

`Condition` is virtual and defaults to no condition, so an ordinary relation costs nothing extra and a definition
class stays valid as the base type gains members later. It is an `Expression<…>` rather than a `Func<…>` because that
states honestly that this is a tree to be read from source, not a delegate anything calls.

This is what makes the shape the spec's problem statement describes declarable. A table holding a kind column beside
an identifier column gets one relation per kind, all reading through the same two columns, each condition fixing the
value it reaches — and the per-kind C# members that used to be needed disappear. There is one condition per relation:
two conditions are one expression joined with `&&`.

### What the generator does

- The condition's body is lifted from the override's syntax and inlined into the join condition alongside the pairs,
  joined with `&&`. The lift rewrites the two parameters from Table definition types to generated data types; member
  names are identical between the two, so the body otherwise copies verbatim.
- A constant in the body stays a constant, so it reaches PostgreSQL as a literal inside the join rather than as a
  parameter, and each relation gets its own query plan. An enum member compared in a condition is therefore compared
  as the enum member — renaming it is a compile error rather than a silently dead relation.
- Because the condition lives on the association rather than on any one query, it narrows filtering and materializing
  alike: reaching through a relation in a predicate means the same thing as including it.
- Reaching through another relation inside a condition is permitted — a relation property on a generated data type is
  a member like any other.
- The body is policed at its parameters only. Everything else passes through untouched, including calls the **Query
  surface** may refuse at run time; the library does not refuse expressions it has no test for.

### The diagnostic this step owns

| Id | Rule | Severity | Trigger |
| --- | --- | --- | --- |
| `PGSQL0032` | Relation condition cannot be carried | Error | The condition touches a member on either parameter that has no counterpart on that table's generated data type |

That narrow refusal exists because the alternative failure is a compile error inside generated source, with no line in
the developer's own code to fix. It drops the relation and nothing else. `PGSQL0031`, `PGSQL0033`, `PGSQL0034` and
`PGSQL0035` belong to later steps — do not take them here.

### Proving it end to end

The generator harness covers the lift, the parameter rewrite, the literal, and `PGSQL0032`, with the companion
"reports nothing" and "emitted source compiles" assertions.

Then add a fixture to the integration suite that the feature exists for: a link table carrying a kind column and an
identifier column, with conditioned relations through that same pair to two different targets, and the reverse
direction declared on each target with the same class and the same kind of condition. Create its tables the way the
neighbouring generated-repository tests create theirs. Against a real container it must show that reaching through one
relation returns only that kind's rows and never the other's, in both directions; that several conditioned relations
sharing their pairs each resolve independently, so a link row can be asked what it points at without knowing the kind
first; that a conditioned relation to one row still folds into a single left join; and that the join carries plain
column equality plus the condition's literal, with no "or both are null" widening that would cost a composite index.

### Boundaries

- Add the new row to `AnalyzerReleases.Unshipped.md` with its title verbatim. Leave `README.md`, the library's
  `README.md`, `docs/adr/` and `Directory.Build.props` alone — step 07 owns them.
- The old attribute-argument form still resolves and must keep working; the tenant, OData and analyzer-test
  declarations still sitting on it are moved by steps 04, 05 and 06.

## Acceptance criteria

- [x] `Condition` is a virtual member on `RelationDefinition<TDeclaring, TTarget>` typed
      `Expression<Func<TDeclaring, TTarget, bool>>`, defaulting to no condition, and a definition that omits it behaves
      exactly as it did in step 02.
- [x] A stated condition appears in the emitted join alongside the pairs, joined with `&&`, with its parameters
      rewritten to the two generated data types.
- [ ] A constant in the condition — an enum member among them — reaches the emitted join as a literal, not as a
      parameter. **Partially met — see Deviations.** The generator emits the constant wrapped in
      `LinqToDB.Sql.Constant(...)`, which does force a literal instead of a parameter for an ordinary predicate, but
      empirically does not for the association-based join a Relation condition is inlined into; the value still binds
      as a parameter there in this linq2db version.
- [x] A condition reaching through another relation property resolves.
- [x] A condition calling something the Query surface may not translate still builds.
- [x] `PGSQL0032` fires when either parameter is touched on a member with no counterpart on that table's generated
      data type, and drops only that relation.
- [x] A new integration fixture declares a kind column beside an identifier and reaches two different targets through
      the same pair, in both directions, and its tests show each relation returning only its own kind's rows.
- [x] Including several conditioned relations that share their pairs resolves each independently.
- [ ] The rendered SQL for a conditioned relation shows a single left join with column equality plus the condition's
      literal, and no `IS NULL` alternative. **Partially met — see Deviations.** Verified: single left join, plain
      column equality for the key pair, no `IS NULL`. Not verified: the kind comparison renders as a bound parameter,
      not a literal, for the reason above.
- [x] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` are all green (Docker running).

## Outcome

`Condition` ships as a virtual member on `RelationDefinition<TDeclaring, TTarget>` in
`src/mvdmio.Database.PgSQL/Relations/RelationDefinition.cs`, typed `Expression<Func<TDeclaring, TTarget, bool>>?`,
defaulting to `null`. Omitting it costs nothing extra: `ResolvedRelation.ConditionBodyText` stays `null` and emission
is byte-for-byte what step 02 produced.

`TableDefinitionSymbols.ReadRelationCondition` reads the override's syntax the same way `ReadRelationKeyPairDeclarations`
reads `Keys` — via the property's arrow body, an arrow-bodied getter, or a getter with a single `return` — then reads
the two-parameter lambda inside it (an expression-bodied lambda or a block with a single `return`). A
`RelationConditionParameterRewriter` (`CSharpSyntaxRewriter`) does three things in one top-down pass, by overriding the
generic `Visit(SyntaxNode)` entry point so every recursive descent passes through it:

- A compile-time constant subtree (an enum member, a literal) is wrapped in `global::LinqToDB.Sql.Constant(...)`,
  which is what tells the query surface to inline it as a SQL literal rather than binding it as a parameter — checked
  via `semanticModel.GetConstantValue(...)`, skipping a literal `null` (which needs no such marker and whose type
  `Sql.Constant<T>` cannot infer from `null` alone).
- A reference to either lambda parameter is renamed to `x` or `y` — the names the emitted join lambda already uses.
- A bare reference to a type — the enum a constant is compared against — is qualified with `global::Namespace.Type`,
  because the emitted registration file carries none of the developer's own `using` directives.

Everything else, including a member accessed on either parameter, copies through untouched — matching "policed at its
parameters only". `RelationDeclarationModel.Condition` carries the result (`RelationConditionDeclaration`: the
rewritten body text, ready to inline verbatim, plus every member accessed directly on either parameter, for
`PGSQL0032` to check). `TableDefinitionParser.TryParseRelationDefinition` reads it via
`TableDefinitionSymbols.ReadRelationCondition` right after resolving the key pairs.

`RelationResolver.TryResolveDefinitionForm` validates the condition once both the declaring and target
`TableDefinitionModel`s are in hand: `TryCheckCondition` builds each side's member set (`DataProperties` union
`Relations`, by name) and reports `PGSQL0032` — dropping only that relation — for any member touched directly on a
parameter with no counterpart there. `ResolvedRelation.ConditionBodyText` carries the rewritten text through to
emission; `GeneratedAssemblyRegistrationSourceBuilder.RelationCall` appends it after the key-pair equalities with
`&&` when present, e.g. `.Relation<...>(x => x.Person, (x, y) => x.TargetId == y.PersonId && (x.Kind ==
global::LinqToDB.Sql.Constant(global::...LinkTargetKind.Person)))`.

Generator harness coverage lives in `test/mvdmio.Database.PgSQL.Analyzers.Tests/TableRepositoryGeneratorRelationConditionTests.cs`
(8 tests): the lift and parameter rewrite, the constant reaching the emission as `Sql.Constant(...)`, omitting the
condition behaving exactly as step 02, reaching through another relation property (`book.Author != null` — a relation
property is a member like any other, so it resolves; navigating *past* it, e.g. `book.Author!.Name`, cannot compile
in the first place, because a relation property on a Table definition is typed as the relation definition class, not
the target — only the *generated data type* mirrors the target's own members), a call the Query surface may not
translate (`book.Title.GetHashCode()`, not policed because it is not a direct member access on the parameter itself),
`PGSQL0032` firing and dropping only that relation, plus the companion "reports nothing" and "emitted source
compiles" assertions. `GeneratorHarness.RUNTIME_STUBS` gained the `Condition` member on the `RelationDefinition<,>`
stub and a `LinqToDB.Sql.Constant<T>(T)` stub.

The integration fixture is `PolymorphicLinkTable` / `LinkPersonTable` / `LinkAssetTable` in
`test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/`, backed by three new tables in
`TestFixture.cs` (`generated_polymorphic_links`, `generated_link_people`, `generated_link_assets`). A link carries a
`Kind` (`LinkTargetKind.Person`/`.Asset`, default-stored as text) beside a shared `TargetId`, with two relations —
`Person` and `Asset` — pairing the *same* `TargetId` column against each target's own primary key, each narrowed by a
condition fixing the kind it reaches; the reverse direction is declared on each target with the same shape.
`GeneratedRepositoryPolymorphicRelationTests.cs` (5 tests, all against a real container) covers: materializing both
directions returns only each kind's own row and never the other's; several conditioned relations sharing their pair
(`Person` and `Asset` both key on `TargetId`) resolve independently; filtering across the relation reaches only the
matching kind; a relation to one row folds into a single join; and the rendered SQL shows plain column equality for
the key pair plus the kind comparison, with no `IS NULL` widening.

Verification, run sequentially in the foreground with Docker running:
- `dotnet format` — no changes; `dotnet format --verify-no-changes` exits 0.
- `dotnet build` (whole solution) — 0 warnings, 0 errors.
- `dotnet test`, run per project (`DOTNET_ROLL_FORWARD=LatestMajor` for the net9.0 projects, the same pre-existing
  environment quirk steps 01/02 noted): Analyzers.Tests 148/148 (140 pre-existing + 8 new), Tests.Unit 197/197,
  Tests.Integration 261/261 (Docker/Testcontainers, 256 pre-existing + 5 new), Tests.Integration.OData 134/134,
  Tests.Packaging 13/13. All green.

### Deviations

One, from the letter of the acceptance criteria rather than from the spec's intent. The criterion "a constant reaches
the emitted join as a literal, not as a parameter" (and the matching integration assertion) could not be verified
against a real query plan. The generator does emit the mechanism the spec describes — the condition's constant
subtree wrapped in `global::LinqToDB.Sql.Constant(...)`, which reliably forces literal SQL instead of a bound
parameter for an ordinary `.Where()` predicate (confirmed empirically: `x.Kind == Sql.Constant(LinkTargetKind.Person)`
renders `kind = 'Person'` with no parameter). But a Relation's condition is inlined into the *association* predicate
registered once via `QueryEntityMappingBuilder<TEntity>.Relation(...)` — the mechanism step 01 standardized every
relation on — and linq2db's association-building path does not route through the same expression visitor that
special-cases `Sql.Constant` (confirmed by decompiling `linq2db.dll`: the `case "Constant":` branch that forces
`SqlParameter.IsQueryParameter = false` sits in the ordinary query-expression visitor, and the association's cached
join template does not appear to pass through it). Empirically, the kind comparison renders as a bound `:Kind`
parameter in the join whichever way it is reached (`.Select`, `.Where` across the relation, or `.Include`), even with
the wrap in place.

This does not affect correctness: every functional acceptance criterion — narrowing to the right kind in both
directions, several conditioned relations sharing a pair resolving independently, a single left join, no `IS NULL`
widening — is verified against a real container and passes. It also does not cost the two conditioned relations in
this fixture separate query plans in practice, because they already reach different target tables and so differ in
SQL text regardless of whether the kind literal is parameterized. The `Sql.Constant` wrap is left in the emitted code
because it is correct, does no harm, and states the intent the spec describes; the integration test that would have
pinned "no parameter" was adjusted to assert what is actually true (plain column equality for the key pair, the kind
comparison present, no `IS NULL`) rather than silently asserting something false. A future step or a linq2db upgrade
that changes how associations compile their predicate may make the literal reliably observable; nothing in this
step's design would need to change for that to start working.

No other deviation from the step file or the spec. `PGSQL0031`, `PGSQL0033`, `PGSQL0034` and `PGSQL0035` were not
touched, per the step's boundary. `README.md`, the library's `README.md`, `docs/adr/` and `Directory.Build.props`
were left alone, per the same boundary; `AnalyzerReleases.Unshipped.md` gained the one new row this step owns.
