# 01 — Every relation registers through the predicate association

Status: pending

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
