# 03 — A relation carries a condition

Status: pending

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

- [ ] `Condition` is a virtual member on `RelationDefinition<TDeclaring, TTarget>` typed
      `Expression<Func<TDeclaring, TTarget, bool>>`, defaulting to no condition, and a definition that omits it behaves
      exactly as it did in step 02.
- [ ] A stated condition appears in the emitted join alongside the pairs, joined with `&&`, with its parameters
      rewritten to the two generated data types.
- [ ] A constant in the condition — an enum member among them — reaches the emitted join as a literal, not as a
      parameter.
- [ ] A condition reaching through another relation property resolves.
- [ ] A condition calling something the Query surface may not translate still builds.
- [ ] `PGSQL0032` fires when either parameter is touched on a member with no counterpart on that table's generated
      data type, and drops only that relation.
- [ ] A new integration fixture declares a kind column beside an identifier and reaches two different targets through
      the same pair, in both directions, and its tests show each relation returning only its own kind's rows.
- [ ] Including several conditioned relations that share their pairs resolves each independently.
- [ ] The rendered SQL for a conditioned relation shows a single left join with column equality plus the condition's
      literal, and no `IS NULL` alternative.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` are all green (Docker running).
