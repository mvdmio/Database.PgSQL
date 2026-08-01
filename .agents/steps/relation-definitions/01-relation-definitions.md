# 01 — Every relation registers through the predicate association

Status: done

## What to build

A prefactor. Nothing a consumer declares changes, and nothing a consumer observes changes. Today a **Relation**
joining on a single pair of columns is registered with the provider through the key-expression association overload,
and only a composite one uses the predicate overload. After this step every relation registers through the predicate
overload, whatever its pair count, always as an outer join.

This is "make the change easy, then make the easy change". The rest of this spec makes a relation a set of column
pairs plus an optional condition, and a condition can only be carried by a predicate. Collapsing to one emission shape
now means the later steps have one shape to extend instead of two, and it settles the only real risk in the change —
whether the provider renders a single-pair predicate association the same way it rendered a key association — while
the tree is otherwise untouched and any difference is unmistakably this step's doing.

The public key-expression overloads on the query mapping builder stay for now. They simply stop being reachable from
generated code; step 06 removes them, together with the rest of the old mechanism.

### Sequencing note

This is a wide refactor sequenced as expand–migrate–contract, and this step is the ground it stands on. The whole
branch keeps the tree green at every step: the attribute-argument declaration form keeps working until step 06, so
steps 02 to 05 add the new form and move fixtures onto it in batches while the old form still resolves. Do not remove
anything from the old path here.

### Boundaries

- Do not touch `README.md`, the library's `README.md`, `docs/adr/` or `Directory.Build.props`. Step 07 owns the
  documentation and the version bump for the whole branch, so no step before it leaves user-facing docs describing a
  half-migrated state.
- No new diagnostic is added or retired here, so `AnalyzerReleases.Unshipped.md` needs nothing.

## Acceptance criteria

- [ ] A relation to one row and a relation to many rows both emit the predicate-based association registration, with
      one column equality per pair combined with `&&`, whatever the pair count.
- [ ] The generator tests that pinned the key-expression call text now pin the predicate call text, and every affected
      test class keeps its companion "a well-formed declaration reports nothing" and "emitted source compiles"
      assertions.
- [ ] No table definition anywhere in the repository changes: the 45 existing relation declarations are untouched.
- [ ] Against a real container the integration suite shows no change: the same rows come back, a relation to one row
      still folds into a single left join, a relation to many rows still costs one statement per level, and the join
      condition is still plain column equality with no "or both are null" alternative.
- [ ] The OData conformance and regression suites pass unchanged.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` are all green (Docker running).

## Outcome

`RelationCall` in `GeneratedAssemblyRegistrationSourceBuilder.cs` no longer branches on `ResolvedRelation.IsComposite`.
It now always builds the `&&`-joined column-equality lambda and emits the predicate overload —
`.Relation<TTarget>(x => x.Property, (x, y) => ...)` — for every relation, whether it joins one pair or several. The
now-unreachable single-pair branch (the three-type-argument key-expression call) is gone from this method, and the
`IsComposite` property on `ResolvedRelation` in `RelationResolver.cs` was removed with it, since nothing reads it any
longer. Nothing else in the resolver changed: key-pair resolution, the arity check, and all existing diagnostics are
untouched.

The public key-expression overloads on `QueryEntityMappingBuilder<TEntity>` are untouched and still resolve for a
hand-written call, per the step's instruction to leave the old path alone until step 06.

Test changes are assertion-only, in the two generator test classes that pinned the old call text:

- `TableRepositoryGeneratorTests.cs` — `ValidRelations_ProduceNoDiagnostics_AndMirrorTheRelationsOntoTheDataTypes` now
  pins the predicate form for the single-pair `Author`, `Editor` and `Books` relations instead of the key-expression
  form. Its companion "reports nothing" and "emitted source compiles" tests
  (`ValidRelations_ProduceCodeThatCompiles`) were already assertion-shape-agnostic and needed no change.
- `TableRepositoryGeneratorCompositeKeyTests.cs` — the existing `CompositeRelation_IsRegisteredThroughThePredicateOverload`
  test needed no change (composite relations already used the predicate form). The defensive
  `CompositeRelation_IsNeverRegisteredThroughAKeyExpression` test was renamed to
  `Relation_IsNeverRegisteredThroughAKeyExpression` and its remark updated, since the claim it guards — no
  three-type-argument key-expression call appears in the registration — now holds for every relation, not only
  composite ones. `CompositeRelations_ProduceCodeThatCompiles` needed no change.

No table definition changed: `git diff --stat` against the pre-step tree touches only the two generator source files
and the two test files above — none of the 45 existing `[Relation]` declarations across the three test projects were
touched.

Verification, run sequentially in the foreground with Docker running:
- `dotnet format` — no changes.
- `dotnet build` — 0 warnings, 0 errors.
- `dotnet test`, run per project with `DOTNET_ROLL_FORWARD=LatestMajor` for the net9.0 projects (a pre-existing
  environment quirk on this machine, not a step concern): Analyzers.Tests 130/130, Tests.Unit 197/197,
  Tests.Integration 256/256 (Docker/Testcontainers), Tests.Integration.OData 134/134, Tests.Packaging 13/13. All
  green; the integration and OData suites needed no changes to pass, confirming no observable behavior moved.
- `dotnet format --verify-no-changes` — exits 0.

### Deviations

None from the spec or the step file. One incidental cleanup beyond the letter of "prefactor, nothing else changes":
`ResolvedRelation.IsComposite` (an internal-only resolver property) was removed because `RelationCall` was its only
reader and it would otherwise sit dead. This is an internal implementation detail with no effect on any consumer,
any generated source, or any later step's contract.
