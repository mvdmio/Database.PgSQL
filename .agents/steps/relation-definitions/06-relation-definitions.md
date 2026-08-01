# 06 — Take the attribute-argument mechanism away

Status: pending

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
