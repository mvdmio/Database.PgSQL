# Admit a nullable unique column as a relation's target

Status: ready-for-agent

## Problem Statement

A developer declares a **Relation** whose **Relation key** pairs a not-null foreign key against a column the target
**Table definition** marks `[Unique]`. Where that unique column can hold null, the build fails with `PGSQL0035` and the
relation is dropped. Nothing generated for that relation exists: no association on the **Query surface**, no member on
the generated data type.

The refusal is wrong on the case it refuses, and it misses the case that is actually broken.

The join such a relation emits is plain equality. PostgreSQL never matches a null with equality, so a target row whose
unique column is null is simply unreachable through the relation. That is well-defined, it costs nothing, and it is
often exactly what the developer means — a code column that most rows carry and a few do not.

Meanwhile a **Relation key** whose *two* columns are both nullable is accepted without a word. That is the shape that
hurts. The query provider widens a comparison between two nullable columns into "equal, or both are null". With nulls
present in the data, the widened form joins every null on one side to every null on the other, which returns rows that
have nothing to do with each other. It also loses the index: ADR 0006 measured the same widening on two fifty-thousand
row tables at 232 times slower on a nested loop and 54 times slower on a hash join.

So the library refuses a relation that works and accepts one that does not.

Two reasons stand behind the refusal, and neither survives inspection.

ADR 0010 gives the first: admitting a nullable target column would need a third `Key(...)` overload, and the ADR chose
refusal over the overload. That premise is simply false, and compiling it settles the matter. The one same-type
`Key(...)` overload already accepts every combination of nullability on the two sides, because the type argument is
inferred from both lambdas at once and settles on whichever of the two types the other converts to. A `long` column
paired against a `long?` column infers `long?` and compiles today. So does a pairing of two `string?` columns, since
nullable annotations on reference types take no part in overload resolution at all. The analyzer is the only thing
stopping any of it.

The diagnostic's own text gives the second: such a column "matches at most one row but may match none for reasons the
relation cannot see". True, and not a fact about uniqueness. A nullable column that is *not* marked `[Unique]` is
accepted today and matches none in exactly the same way.

## Solution

The rule changes what it looks at. A **Relation key** is refused when **both** of its columns can hold null. Whether
either column is unique stops mattering.

For the developer this means:

- A not-null foreign key paired against a nullable `[Unique]` column builds, silently, and generates everything a
  relation generates. Target rows whose unique column is null are unreachable through it, which is the plain meaning of
  the equality the join emits.
- A pair whose two columns can both hold null fails the build, wherever it appears, unique column or not. The message
  names the fix: claim one side cannot hold null, or pair a column that cannot.
- The rule reads the **Nullability claim** rather than the property's C# type, so `[Column(NotNull = true)]` is a real
  answer to it — including in a file where nullable annotations are switched off, which is the one place the C# type
  cannot carry the fact at all.

Nothing about `Key(...)` changes. It already accepts every nullability combination, so the whole change is the analyzer
rule, the documents that describe it, and the version.

## User Stories

1. As a developer with a code column most rows carry, I want to pair a not-null foreign key against that nullable
   `[Unique]` column, so that I can declare the relation my schema already has.
2. As a developer, I want that relation to generate everything an ordinary relation generates, so that the target
   appears on the generated data type and can be filtered, ordered and materialized across.
3. As a developer, I want no warning on that relation, so that my build stays quiet about a shape that is correct.
4. As a developer whose foreign key is a `long` and whose target unique column is a `long?`, I want the pair accepted,
   so that the value-type case is no harder to write than the reference-type one.
5. As a developer, I want a pair of two nullable columns to fail the build, so that I never ship a join that returns
   rows which should not have joined.
6. As a developer, I want that failure to name my fix, so that I do not have to read the library's source to learn
   what to do.
7. As a developer, I want `[Column(NotNull = true)]` on one side to clear the failure, so that I can state a fact about
   my database that its C# type cannot carry.
8. As a developer working in a file with nullable annotations switched off, I want an unannotated `string` to count as
   able to hold null, so that the rule reads what my file actually says rather than assuming what it cannot.
9. As a developer, I want the failure reported once per offending pair, so that a relation with two bad pairs tells me
   about both rather than making me fix them one build at a time.
10. As a developer, I want a relation refused for this reason to take only itself down, so that the rest of my table
    still generates and my build reports one problem rather than a cascade.
11. As a developer whose target unique column is nullable, I want the relation to still count as reaching one row, so
    that declaring it as a relation to one row does not also warn me under `PGSQL0031`.
12. As a developer, I want the emitted join for the permitted shape to be plain equality, so that PostgreSQL can use
    the index behind the unique column.
13. As a developer, I want a target row whose unique column is null to be unreachable through the relation rather than
    matched arbitrarily, so that what I read matches what the SQL says.
14. As a developer, I want a row whose foreign key points at no target row to survive the query with nothing attached,
    so that a relation stays an outer join as it always has.
15. As a developer with two genuinely nullable columns and no way to claim otherwise, I want the library to say plainly
    that the relation cannot be declared, so that I learn it at build time instead of from a slow, wrong result set.
16. As a developer upgrading, I want a pair of two nullable columns that built before to fail now, so that a join I
    never knew was widened stops being silent.
17. As a developer upgrading, I want a relation refused before for a nullable unique target to start building, so that
    a workaround I wrote for it becomes removable.
18. As a maintainer, I want one rule covering every **Relation key**, so that the library does not report half the
    instances of a problem it knows about.
19. As a maintainer, I want the rule to read the same nullability the query provider is told, so that what the analyzer
    predicts and what the provider does cannot drift apart.
20. As a maintainer, I want `PGSQL0035` to keep its id, so that the diagnostic numbering does not grow a hole for a
    rule nothing in production depends on.
21. As a maintainer, I want the reasoning recorded in an ADR, so that a future reader finds why ADR 0010's refusal was
    replaced rather than rediscovering the measurement behind it.
22. As a maintainer, I want ADR 0010 left as written with a pointer forward, so that the record of why refusal looked
    right at the time survives alongside the correction.
23. As a consumer reading the README, I want the nullability paragraph to say that a **Relation key** reads the same
    claim, so that I learn the rule where I learn the claim.
24. As a consumer, I want the glossary to describe the rule that ships, so that **Relation key** and **Nullability
    claim** agree with the build.

## Implementation Decisions

**The rule moves from the target column to the pair.** The relation resolver's key-pair check stops asking whether the
target column is unique and nullable. It asks whether both columns of the pair can hold null. Uniqueness is no longer
an input to it.

**The rule reads the Nullability claim, not the C# type.** The current check reads the property's type-level
nullability. That is the wrong fact: what the query provider is told comes from the **Nullability claim**, registered
per column when the mapping is built. A column declared `[Column(Null = true)]` over a non-nullable `string` is
nullable to the provider and not nullable to the type, and it is the provider's view that decides whether the join
widens. The rule switches to the claim. The primary-key nullability check keeps reading the C# type, because a key
member's type is the fact there rather than a claim about it.

**`PGSQL0035` is reused, with a new title and message.** Nothing in production depends on its current meaning, so no
suppression can be silently inherited. The message states the shape and the fix: claim one side cannot hold null, or
pair a column that cannot.

**Severity and blast radius are unchanged.** The rule stays an error and still drops only the relation, leaving the
rest of the table to generate — ADR 0005's precedent for a relation-level problem. It reports once per offending pair,
since each pair is a separate mistake, and it still short-circuits: a relation dropped for this reason reports nothing
further about target uniqueness or tenancy pairing, both of which are moot once the relation is gone.

**A nullable unique column still satisfies the uniqueness claim.** `PGSQL0031` asks whether the paired target columns
reach at most one row. A nullable `[Unique]` column does, so it keeps counting, and a relation to one row against it
warns about nothing.

**`RelationDefinition<,>` is not touched.** ADR 0010's claim that a nullable target side needs a third `Key(...)`
overload was checked by compiling all four nullability combinations against the current signatures, and every one of
them builds. The same-type overload infers its type argument from both lambdas together and settles on whichever type
the other converts to, so a `long` column paired against a `long?` column infers `long?` and binds. The key expressions
are read from source syntax rather than from a compiled expression tree, so the widening conversion this puts in the
tree reaches nothing that cares about it.

**The type system cannot carry the rule at all.** Because one overload accepts every combination, no pairing can be
made a compile error by any arrangement of overloads. The analyzer is the only place the rule can live.

**A Relation condition cannot rescue a refused pair.** A developer can already write a condition excluding nulls, and
it removes the wrong rows without removing the widening, so the index is still lost. The pair is refused whatever the
condition says. This differs deliberately from `PGSQL0031`, which a condition *can* rescue: there the condition
supplies the missing guarantee outright, here it recovers only half of it.

**Nothing is done about the whole-context null-comparison mode.** The query provider offers no way to switch the
widening off for one join; the only lever is a context-wide option, and changing it would change what every filter a
consumer writes against null means. ADR 0004 chose the current mode deliberately and the OData front-end depends on
it. This is recorded in the ADR as considered and rejected, so the next reader does not re-derive it.

**The rule does not reach into a Relation condition's own comparisons.** A condition is an arbitrary expression and can
compare two nullable columns exactly as a key pair can, widening the join the same way. Judging expression shapes would
start the library down the road of permitting only the shapes it recognises, which is the opposite of how the
**Translation boundary** is drawn everywhere else. Key pairs only.

**Documentation and version.** A new ADR records the decision; ADR 0010 stays as written and gains a pointer to it, the
same way ADR 0005 and ADR 0006 point at ADR 0010. The README's existing paragraph about a column's nullability gains
one sentence saying a **Relation key** reads the same claim and that two nullable sides are refused; relations are not
otherwise shown in the README and this change does not add them. `CONTEXT.md`'s **Relation key** and **Nullability
claim** entries are updated to describe the rule that now ships. `<PgSqlVersion>` goes from 0.37.0 to 0.38.0: no public
API changes, but the diagnostic change breaks builds that pass today, which is a MINOR bump under this project's pre-1.0
rule.

## Testing Decisions

A good test here states what a developer observes: which diagnostics a piece of source earns, whether a relation
reaches the generated output at all, and what SQL the **Query surface** emits. None of it reaches into how the resolver
decides. Two existing seams cover the whole change and no new seam is needed.

**The generator harness, for the rule.** The analyzer tests drive the whole generator from a C# source string and read
back diagnostics and generated sources. Prior art is the relation key claims test class, which already holds the
`PGSQL0035` cases and the `PGSQL0031` ones beside them. New and changed cases there:

- A not-null foreign key against a nullable `[Unique]` target column reports nothing and registers the relation. This
  is the existing refusal test inverted — it asserts today that the relation is absent from the registration, and it
  will assert that it is present.
- The existing non-nullable-target case stays as it is, unchanged and still silent.
- A pair of two nullable columns reports `PGSQL0035` and drops that relation only, with the rest of the table still
  generated.
- The same pair with `[Column(NotNull = true)]` on one side reports nothing.
- The same pair in a file with nullable annotations switched off, where an unannotated `string` counts as able to hold
  null, reports `PGSQL0035` — and clears once the attribute states otherwise.
- A pair of two nullable columns against a target column that is *not* unique reports `PGSQL0035`, which is the case
  that is silent today.
- A relation with two offending pairs reports twice.
- A relation to one row against a nullable `[Unique]` column reports no `PGSQL0031`.
- A refused pair carrying a **Relation condition** that excludes nulls still reports `PGSQL0035`.
- Every nullability combination of a value-typed pair still compiles, which is what makes the analyzer the thing that
  reports the bad one rather than the compiler.

**The generated-repository integration tests, for the emitted SQL.** These run against real PostgreSQL through
Testcontainers and render a query's SQL through the existing query diagnostics helper. Prior art is the composite-key
test class, which pins this exact concern: it asserts a cross-table equality per key column and that the SQL contains
no `IS NULL`, with the reason stated in the assertion. New cases in the same style:

- Filtering across a relation whose target is a nullable `[Unique]` column renders a plain cross-table equality and no
  `IS NULL`, because the widened alternative is what would cost the unique index.
- The relation without a filter on the far side renders an outer join, which is what says a row whose foreign key
  matches nothing survives with nothing attached.
- Materializing the relation reaches the related row, and a target row whose unique column is null is reached by
  nothing.

**The fixture carries the shape.** The integration fixture that creates the generated-repository tables gains a
nullable unique column on the target side and a not-null column of the same type on the declaring side, with a real
unique constraint in the DDL — PostgreSQL admits many nulls under one, which is what makes the fixture honest about the
shape being tested. The fixture's own table definitions are compiled by the real generator, so a fixture declaring this
relation also demonstrates that the pairing compiles with no library change.

**No test asserts the performance numbers.** ADR 0006 measured the widening once and the measurement stands in the
record. What the tests pin is the SQL shape the measurement was about, which is the thing that can regress.

## Out of Scope

**The generated lookup for a nullable unique column.** A `[Unique]` column also generates a lookup and a delete named
after it. Where the column can hold null those take a nullable argument, and the SQL compares with equality, so passing
null quietly matches no row instead of finding the row whose column is null. Same theme, different surface, and its own
answer. Not touched here.

**Comparisons inside a Relation condition.** Covered under Implementation Decisions: the rule reads key pairs only.

**The whole-context null-comparison mode.** Also covered above: considered and rejected, unchanged.

**Any database-level guarantee.** A **Relation** still creates no foreign key, no unique index and no null constraint,
and still verifies nothing against the real table. A developer who wants the database to enforce any of it writes a
migration by hand, exactly as before.

**The primary-key nullability rule.** A key member still cannot hold null and that check still reads the property's
C# type. Untouched.

**Removing the redundant `Key(...)` overload.** Both existing overloads stay exactly as they are, pending the open
question below.

## Further Notes

**Open question: does the nullable-left `Key(...)` overload get removed?** Checking ADR 0010's overload claim turned up
that the second overload — a nullable declaring column against a not-null target — earns nothing. Compiling every
nullability combination with that overload deleted succeeds, because the same-type overload already covers all four.
Removing it would leave one overload and one story about how a pair is written, and would delete a documentation comment
that describes a rule the library does not actually have. Against that, it is a published member of a public abstract
class: source-compatible to remove, since every call rebinds to the remaining overload, but binary-breaking for an
already-compiled consumer. Nothing in this spec depends on the answer, which is why it is a question rather than a
decision here.

The sharpest edge of this design is deliberate and worth stating plainly: where two columns are genuinely nullable in
the database and neither side can honestly be claimed otherwise, the relation cannot be declared at all. There is no
escape hatch, because every candidate one was rejected for a reason — a **Relation condition** recovers the rows but
not the index, and the context-wide comparison mode would change the meaning of every filter a consumer writes. The
honest position is that the join the library would emit for that shape is wrong, so refusing it is better than
emitting it. If that turns out to block a real schema, the answer is a new decision about how to compare two nullable
columns, not a quiet exception to this one.

Worth noting for whoever implements it: the two notions of nullability in the analyzer look interchangeable and are
not. One is the property's C# type, the other is the **Nullability claim** the mapping registers. They agree for most
columns and disagree for the two that matter here — a column claimed able to hold null over a non-nullable `string`,
and an unannotated `string` in a file where nullable annotations are switched off. Reading the wrong one is how the
current rule came to check something the query provider never sees.
