---
status: accepted
---

# Refuse a Relation key pair by nullability, not by uniqueness

[ADR 0010](0010-relation-definitions.md) refused a **Relation key** pairing against a `[Unique]` target column that
could hold null (`PGSQL0035`), reasoning that such a column "matches at most one row but may match none for reasons
the relation cannot see." That reasoning does not survive inspection, and the rule it produced refuses a shape that
works while accepting one that does not.

A nullable `[Unique]` target is not the problem. PostgreSQL's `UNIQUE` constraint admits any number of nulls, and the
join a **Relation key** emits is plain equality — it never matches a null on either side. A not-null foreign key
against a nullable `[Unique]` column is exactly as reachable as a not-null foreign key against any other column: rows
whose target is null are simply unreachable through it, which costs nothing and is often the developer's intent. The
diagnostic's own justification — "may match none" — is true of every nullable column, unique or not, and was never a
fact about uniqueness at all.

Meanwhile a **Relation key** whose *two* columns are both nullable built silently. That is the shape that hurts: the
query provider widens a comparison between two nullable columns into "equal, or both are null", joining every null on
one side to every null on the other and losing the index behind either column. [ADR 0006](0006-composite-primary-keys.md)
measured the same widening at 232x slower on a nested loop and 54x on a hash join, over two fifty-thousand-row tables.

The other premise behind ADR 0010's refusal was that admitting a nullable target column would need a third `Key(…)`
overload, and the ADR chose refusal over adding one. That premise is false, and compiling it settles the matter: the
one same-type `Key<TValue>(...)` overload already accepts every nullability combination on the two sides, because its
type argument is inferred from both lambdas at once and settles on whichever of the two types the other converts to.
A `long` column paired against a `long?` column infers `long?` and compiles today, with no change to
`RelationDefinition<,>`. Nothing in the type system stops any pairing — no arrangement of overloads can make one a
compile error — so the analyzer was always the only place a rule like this could live.

## Decision

The rule moves from the target column to the pair. A **Relation key** is refused when **both** of its columns can
hold null. Whether either column is `[Unique]` no longer has any bearing on it.

The rule reads the **Nullability claim** each side registers, not the property's C# type. The claim is what the query
provider is actually told, and the two disagree exactly where it matters: a column claimed nullable over a
non-nullable `string`, and an unannotated `string` in a file where nullable annotations are switched off. Reading the
type instead of the claim is how the retired rule came to check something the query provider never sees, and it is
also the only way `[Column(NotNull = true)]` can clear the failure in a nullable-oblivious file, where the C# type
carries nothing to read.

Which fix clears a given pair therefore depends on what its sides' types already say, and the message says so rather
than naming one fix for every shape. Where a side's type can hold null — a `string?`, a `long?` — the type is the fix,
because `[Column(NotNull = true)]` over such a type contradicts it: `PGSQL0021` reports the contradiction, the claim is
dropped, and the column stays nullable, so the attribute would leave the developer with two errors instead of none.
Where the type says nothing at all — an unannotated reference type in a file with nullable annotations switched off —
or where the column is claimed `[Column(Null = true)]` over a type that cannot hold null, the claim is the only thing
that can carry the fact and `[Column(NotNull = true)]` clears the failure outright. `PGSQL0021`'s contradiction rule is
deliberately not relaxed to make the attribute win over a nullable type: that rule is older than this one, it governs
every column rather than only relation keys, and re-deciding it is a separate decision.

`PGSQL0035` keeps its id, its `Error` severity and its blast radius — it drops only the relation it names, leaving the
rest of the table to generate, exactly as ADR 0010 recorded. Its title, message and description change, because
nothing in production depends on the retired meaning. It reports once per offending pair, since each pair is a
separate mistake, and a relation refused for this reason still reports nothing further about target uniqueness or
tenancy pairing — both are moot once the relation is gone. `PGSQL0031`'s uniqueness claim is untouched: a nullable
`[Unique]` column still counts as unique, so a relation to one row against it still warns about nothing. The
primary-key nullability rule (`PGSQL0020`) is untouched too and keeps reading the C# type, because a key member's type
is the fact there rather than a claim about it.

## Considered and rejected

- **A `Relation condition` excluding nulls, as an escape hatch for a refused pair.** A developer can already write a
  condition and narrow out the null rows, but that removes the wrong rows without removing the widening — the join
  still loses the index. Unlike `PGSQL0031`, which a condition *can* rescue because there the condition supplies the
  missing uniqueness guarantee outright, here it only recovers half the problem. The pair is refused whatever the
  condition says.
- **A context-wide null-comparison mode**, switched off for this one case. The query provider offers no per-join
  lever — only a context-wide option — and changing it would change what every filter a consumer writes against null
  means. [ADR 0004](0004-linq2db-as-the-queryable-provider.md) chose the current mode deliberately, and the OData
  front-end depends on it staying put.
- **Reaching into a Relation condition's own comparisons**, to catch a widened join hidden there instead of in the
  key pairs. A condition is an arbitrary expression, and judging its shape would start the library down the road of
  permitting only the expression shapes it recognises — the opposite of how the **Translation boundary** is drawn
  everywhere else. The rule reads key pairs only.

## Consequences

- **`PGSQL0035` keeps its id**, with `Error` severity and relation-only blast radius unchanged; its title, message and
  description now state the shape and every fix that actually clears it — give one side a type that cannot hold null,
  claim `[Column(NotNull = true)]` on a side whose type cannot say it, or pair a column that cannot hold null.
- **`PGSQL0020` still reads the property's C# type**, unaffected by this decision.
- **`RelationDefinition<,>` is untouched.** No new `Key(…)` overload, no signature change; the shape this ADR admits
  already compiled.
- **The sharp edge is deliberate.** Where two columns are genuinely nullable in the database and neither side can
  honestly be claimed otherwise, the relation cannot be declared at all — there is no escape hatch, because every
  candidate one above was rejected on its own merits. If that blocks a real schema, the answer is a new decision about
  how to compare two nullable columns, not a quiet exception to this one.
- **API surface.** No public API changes — `PGSQL0035`'s id, category and severity are unchanged, only its message
  changed. The rule change breaks a build that passed today wherever two genuinely nullable columns were paired, which
  is a MINOR bump under this project's pre-1.0 rule: `0.37.0` to `0.38.0`.
