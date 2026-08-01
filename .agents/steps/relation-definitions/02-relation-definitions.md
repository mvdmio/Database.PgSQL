# 02 — Declare a relation as a class

Status: done

## What to build

A developer declares a **Relation** by writing a **Relation definition**: a class deriving from
`RelationDefinition<TDeclaring, TTarget>` that names both **Table definitions** in its type arguments and states the
**Relation keys** as pairs of property expressions. The **Relation property** is typed as that class, or as a
supported collection of it, and that type alone is what makes it a relation — no attribute is needed and the
cardinality is read the way it always was.

```csharp
[Table("books")]
public partial class BookTable
{
   [PrimaryKey] public long BookId { get; set; }
   public long? AuthorId { get; set; }

   public AuthorRelation? Author { get; set; }

   private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AuthorId, y => y.AuthorId),
      ];
   }
}
```

The class may be nested inside the Table definition it belongs to — private is fine, because nothing instantiates it
and nothing calls it — or declared anywhere else, because the type arguments say which tables are involved wherever it
lives. `Key(…)` is generic over the column's type, so a pair whose two sides hold different types does not compile,
and it has exactly two overloads: matching types, and a nullable left side against a non-nullable right, which is the
ordinary outer-join case. There is deliberately no third overload for a nullable right side; step 04 refuses that
shape outright.

The order the pairs are written in carries no meaning — they are joined with `&&` — and neither side has to be the
one that "owns" the foreign key, so both directions of a relation read alike. A relation stays one-directional:
declaring one never creates the other.

The old attribute-argument form keeps working unchanged. That is what keeps the tree green while the rest of the
branch lands: this step adds a second, type-driven path beside the attribute-driven one and removes nothing. Step 06
takes the old one away.

### What the generator does

- The relation-property split becomes type-driven. A property whose type derives from `RelationDefinition<,>`, or is
  one of the already-supported collection types over such a type, is a relation property. `HashSet<T>` stays
  unsupported, as it is today.
- The target and the declaring table come from the type arguments rather than from the property's type and its
  enclosing class. The target must be a `[Table]` class in the same compilation.
- Key pairs are read from the `Keys` override's syntax. Each side must be a direct property reference resolving to a
  mapped column of its own table.
- Emission is the predicate association step 01 settled on: one equality per pair, combined with `&&`, always an outer
  join.
- The generated data type is unchanged in shape: the relation still appears there as the target's generated data type,
  or a list of it — never as the relation definition class.

### Diagnostics this step owns

New ids, continuing from `PGSQL0028` in the order the spec fixes. Later steps take the ones not listed here, so do not
renumber.

| Id | Rule | Severity | Trigger |
| --- | --- | --- | --- |
| `PGSQL0028` | Relation declaring table mismatch | Error | The `TDeclaring` type argument is not the Table definition the relation property is declared on |
| `PGSQL0029` | Relation states no keys | Error | The `Keys` override yields no pairs |
| `PGSQL0030` | Relation key is not a column reference | Error | Either side of a pair is not a direct reference to a mapped column of that table |

Reshaped: `PGSQL0014` (relation target is not a Table definition) reads the target from the type argument, because no
generic constraint can express "is a `[Table]` class in this compilation". `PGSQL0015` (relation to one row must be
nullable) keeps applying to the relation property. `PGSQL0016`, `PGSQL0017` and `PGSQL0018` keep their meanings for a
definition-typed property.

Blast radius is unchanged from ADR 0005: a relation-level error drops that relation and nothing else, so the table
still generates and the developer reads one message rather than a wall of type-not-found errors.

### Proving it end to end

The generator harness is the seam for everything on the declaration side, and its runtime stubs must gain
`RelationDefinition<,>`, `RelationKey` and `Key(…)` so a test never passes on a shape that would not compile for a
real consumer. Keep the "emitted source compiles" assertion on every new test class — that is the guard.

Then convert the author-and-book fixtures in the integration suite — the author, book, book-tag and tag tables, nine
declarations between them — to the new form, and leave their tests alone. Those declarations are already exercised
against a real container, so passing them unchanged is what shows the new form works end to end rather than only in
the harness. They cover a relation to one row, a relation to many, a table relating to itself in both directions, and
two relations at the same target.

### Boundaries

- The tenant tables in the integration suite, the OData fixtures and the analyzer test sources stay on the old form;
  steps 04, 05 and 06 move them.
- Add the three new rows to `AnalyzerReleases.Unshipped.md` with their titles verbatim. Leave `README.md`, the
  library's `README.md`, `docs/adr/` and `Directory.Build.props` alone — step 07 owns them.

## Acceptance criteria

- [ ] `RelationDefinition<TDeclaring, TTarget>` ships in the library: abstract `Keys` returning
      `IReadOnlyList<RelationKey>`, `protected static Key(…)` with exactly two overloads, `RelationKey` an opaque value
      nothing reads at run time. Public surface carries `[PublicAPI]` and XML docs.
- [ ] A property typed as a relation definition is a relation to one row; a supported collection of one is a relation
      to many; neither needs `[Relation]`, and writing `[Relation]` on one is still accepted.
- [ ] A relation definition nested privately inside its Table definition and one declared outside it both resolve.
- [ ] The pairs' written order does not change the emitted join, and a pair stating the target side first resolves the
      same as one stating the declaring side first.
- [ ] Mismatched pair types fail to compile rather than reaching a diagnostic; a nullable left side against a
      non-nullable right compiles.
- [ ] `PGSQL0028`, `PGSQL0029` and `PGSQL0030` each fire on their trigger, drop only the relation they describe, and
      leave the rest of the table generating.
- [ ] `PGSQL0014` reports a target read from `TTarget`, and `PGSQL0015` still reports a non-nullable relation to one
      row.
- [ ] The harness stubs carry the new runtime types, and every new generator test class asserts that the emitted
      source compiles.
- [ ] The author, book, book-tag and tag fixtures in the integration suite are declared in the new form and their
      existing tests pass unchanged.
- [ ] Every relation still declared in the old attribute-argument form still resolves and still works.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` are all green (Docker running).

## Outcome

`RelationDefinition<TDeclaring, TTarget>` and `RelationKey` ship in
`src/mvdmio.Database.PgSQL/Relations/RelationDefinition.cs`. `Keys` is abstract; `Key(…)` has exactly the two
overloads the spec's settled decision allows — matching types, and a nullable left side (`where TValue : struct`)
against a non-nullable right. Both carry `[PublicAPI]` and XML docs. `RelationKey`'s constructor is `internal`, so
nothing outside the library can construct one; the classes exist purely for the generator to read from source, and
`Key(…)`'s body is never executed.

The generator's relation-property split is now type-driven, alongside the existing attribute-driven path:

- `TableDefinitionSymbols.IsRelationProperty` treats a property as a relation if its type — or its collection element
  type, for a relation to many — derives from `RelationDefinition<,>`, in addition to the existing
  `[Relation]`-attribute check. `TryGetRelationTarget` gained two `out` parameters (`relationDefinition`,
  `declaringTypeArgument`) so `TableDefinitionParser` can tell which form it is looking at and, for the new form, read
  `TDeclaring` off the closed base type.
- `TableDefinitionSymbols.ReadRelationKeyPairDeclarations` reads the `Keys` override's syntax — however it is
  written (an arrow-bodied property, an arrow-bodied getter, or a getter with a single `return`) and whatever
  collection literal it returns (`[…]`, `new[] {…}`, `new List<T> {…}`, or a plain initializer) — and resolves each
  `Key(…)` call's two lambda arguments to the property they name, via the semantic model of whichever file the
  relation definition class lives in (never assumed to be the same file as the table). Either side is reported back
  as `null` when it is not a direct, parameter-rooted property access, which is what `PGSQL0030` reports on.
- `RelationDeclarationModel` carries the new form's pairs in `KeyPairs` (`null` for the old form) alongside the old
  form's `ForeignKeyPropertyNames`; `IsDefinitionForm` is what a caller reads rather than checking either for
  `null` directly.
- `RelationResolver.TryResolveDefinitionForm` resolves each pair's two property names against the declaring and
  target models' own `DataProperties` directly — no foreign-key/primary-key side to work out from cardinality, since
  each pair already names both sides itself, unlike the old form's positional-against-the-primary-key scheme.
  `ResolvedRelation` gained a second constructor taking pre-built `JoinedKeyPair`s for this. Emission is untouched:
  both forms register through the same predicate overload step 01 settled on.
- The three new diagnostics fire exactly where the step file describes: `PGSQL0028` when `TDeclaring` is not the
  table the property is declared on, `PGSQL0029` when `Keys` yields no pairs, `PGSQL0030` when a pair's side is not a
  direct property reference (checked syntactically for the declaring side and cross-table for the target side, both
  through the same descriptor). `PGSQL0014` and `PGSQL0015` are reused unchanged, now firing for a definition-typed
  property too. Each drops only the relation it describes, per ADR 0005's blast radius. All three rows are in
  `AnalyzerReleases.Unshipped.md` with their titles verbatim.

One shape check had to be relaxed, and it is the one deviation from the letter of the step file: **a relation
property's own accessibility is no longer checked (`PGSQL0017`'s trigger dropped "must be public"), only that it has
a getter and a setter.** The step's own example — nesting a relation definition as a `private` class and typing the
property `public` — does not compile in plain C#: a public member cannot expose a less accessible type (`CS0053`),
and this is a hard language rule this library cannot suppress. Since a relation property is purely declarative and
nothing ever reads or writes it at run time (only its type is read, by the generator, at compile time), there is
nothing lost by not requiring `Public`: the fix is that a privately-nested relation definition needs its carrying
property to be non-public too, which is ordinary, valid C#, and still exercises every "private is fine" claim the
step and spec make. `TableDefinitionSymbols.IsSupportedRelationPropertyShape` (no accessibility check) replaces
`IsSupportedProperty` (which still requires `Public`, unchanged) for relation-property shape validation; column
validation is untouched. The four integration fixtures below use this — their relation properties are `private` — and
it has no effect on any existing test, because those tests only ever read the mirrored property on the *generated*
data type (`AuthorData.Books`, not `AuthorTable.Books`), which the generator always emits `public` regardless of the
original property's own accessibility.

The generator harness (`GeneratorHarness.RUNTIME_STUBS`) gained matching stubs for `RelationDefinition<,>`,
`RelationKey` and the two `Key(…)` overloads; `RelationAttribute`'s stub is unchanged (still the params-array
constructor), since the old form keeps working unchanged until step 06. A new test class,
`TableRepositoryGeneratorRelationDefinitionTests.cs` (10 tests), covers: a relation to one row and to many rows, a
definition class nested privately and one declared externally, the nullable-left `Key` overload (exercised through
`BookTable.AuthorId : long?` against `AuthorTable.AuthorId : long`), pair-order independence (a composite two-pair
relation asserted with both pair orders, each producing both join clauses), the bare `[Relation]` marker still being
accepted on a definition-typed property, and each of `PGSQL0028`, `PGSQL0029`, `PGSQL0030`, plus `PGSQL0014` and
`PGSQL0015` reused for the new form. Every test class carries the "emitted source compiles" companion assertion,
including two dedicated to the composite-pair-order fixture, per the testing decision that a stub drifting from the
real type would let a test pass on a shape no real consumer could compile.

The author, book, book-tag and tag fixtures in `test/mvdmio.Database.PgSQL.Tests.Integration/GeneratedRepositories/`
(nine relations: a relation to one row, a relation to many, a table relating to itself in both directions, and two
relations at the same target) are converted to the new form with no other change, and their existing tests pass
unchanged — 256/256, confirming the new form works end to end against a real container, not only in the harness. The
OData fixtures and the analyzer test sources stay on the old form, per this step's boundaries; steps 04/05/06 move
them.

Verification, run sequentially in the foreground with Docker running:
- `dotnet format` — reformatted only the files this step touched (using-directive order); `dotnet format
  --verify-no-changes` then exits 0.
- `dotnet build` (whole solution) — 0 warnings, 0 errors.
- `dotnet test`, run per project (`DOTNET_ROLL_FORWARD=LatestMajor` for the net9.0 projects, the same pre-existing
  environment quirk step 01 noted): Analyzers.Tests 140/140 (130 pre-existing + 10 new), Tests.Unit 197/197,
  Tests.Integration 256/256 (Docker/Testcontainers), Tests.Integration.OData 134/134, Tests.Packaging 13/13. All
  green.

### Deviations

One, covered above: `PGSQL0017`'s trigger no longer requires a relation property to be `public` — only that it has a
getter and a setter — because the step's own nested-private-class example cannot compile with a `public` property at
all (`CS0053`), regardless of anything this library's analyzer does. Nothing else deviates from the step file or the
spec: `RelationAttribute` is untouched, the old attribute-argument form is untouched and its 45 pre-existing
declarations across the three test projects were not touched by this step (only the nine converted here), and no
diagnostic outside `PGSQL0028`/`PGSQL0029`/`PGSQL0030` (plus the reused `PGSQL0014`/`PGSQL0015`) was added or
reshaped — the tenancy-pairing reshape and the condition-carrying diagnostics the spec describes belong to later
steps and are untouched here.
