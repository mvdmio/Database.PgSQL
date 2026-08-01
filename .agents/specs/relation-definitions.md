# Declare a relation as a class, and let it carry a condition

Status: ready-for-agent

## Problem Statement

A **Relation** is fixed by the type of its **Relation property**. One property names one target **Table definition**, and
the foreign-key property names on its attribute are matched in **Key order** against that target's primary key. Two
relation properties may name the same foreign-key properties, but nothing tells them apart, so both resolve every row.

That leaves a common table shape undeclarable. A table holds a pair of columns where one names a kind and the other
holds an identifier, and the kind decides which table the identifier belongs to:

```
account_id  bigint
owner_id    uuid
target_kind text     -- 'Person', 'Asset', 'Incident', …
target_id   uuid
```

What a developer wants to declare against it is one relation per kind, all reading through the same two columns and
separated by the value of the kind column. Rails calls this `belongs_to :polymorphic` and Hibernate calls it `@Any`.
Entity Framework Core has no first-class support for it, and neither does this library.

Without it, a developer reaches those targets by adding a real column per kind — a stored generated column holding the
identifier only when the kind matches — and declaring an ordinary relation against each one. One compliance
application meeting this shape has six link tables and three ordinary tables carrying the pair, with a kind column
selecting among roughly twenty-three tables. That is about ninety C# members: forty-five foreign-key properties and
forty-five relation properties, each mirrored onto a generated data type and from there onto whatever a **Query
front-end** builds. The database columns are not the library's fault and do not go away. The ninety C# members would.

The narrower problem underneath it is that a relation can say only two things — which columns, and which target — and
both are said in two different places. The target lives in the property's type and the columns live in strings on an
attribute, resolved by the analyzer. There is nowhere to put anything else, and no third thing can be added without
another attribute argument that the compiler cannot check.

## Solution

A relation is declared by a class deriving from `RelationDefinition<TDeclaring, TTarget>`. The class names both tables
in its type arguments, states the column pairs that resolve the relation, and states an optional condition that
narrows it further. The relation property is typed as that class, or as a list of it.

```csharp
[Table("links")]
public partial class LinkTable
{
   [PrimaryKey, Column(Tenancy = true)] public Guid AccountId { get; set; }
   [PrimaryKey] public Guid LinkId { get; set; }
   public TargetKind TargetKind { get; set; }
   public Guid TargetId { get; set; }

   // As accessible as the nested definition class each is typed as, and no more: C# refuses a public property
   // typed as a private nested class (CS0053). The build does not require either to be public.
   private PersonRelation? Person { get; set; }
   private List<AuditRelation> Audits { get; set; } = [];

   private class PersonRelation : RelationDefinition<LinkTable, PersonTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AccountId, y => y.AccountId),
         Key(x => x.TargetId,  y => y.PersonId),
      ];

      public override Expression<Func<LinkTable, PersonTable, bool>> Condition
         => (link, person) => link.TargetKind == TargetKind.Person;
   }
}
```

This replaces the attribute-argument mechanism rather than sitting beside it. There is one way to declare a relation.
The class is purely declarative in the same sense a Table definition is — never instantiated, never executed. The
generator reads what it says from source and inlines it into the association it already registers.

Three things follow. The column pairs are expressions, so a rename cannot silently break a relation and a type
mismatch does not compile. The condition is ordinary C# checked by the compiler where it is written. And two relations
that pair the same columns can now reach different rows, which is the shape the problem statement describes: the kind
column is compared to a different value in each relation's condition, and the per-kind C# members disappear.

The library still creates nothing and verifies nothing against the real database. A developer who wants the database
to refuse a link pointing at a row that does not exist still writes the generated column and its foreign key in a
migration by hand. This feature is about traversal, and about whether those columns also have to exist in C#.

## User Stories

### Declaring a relation

1. As a developer declaring a relation, I want to write it as a class naming both tables in its type arguments, so
   that everything the relation says lives in one place.
2. As a developer, I want to nest that class inside the Table definition it belongs to, so that a reader of one file
   sees the whole declaration.
3. As a developer, I want to be free to put that class elsewhere, so that a large Table definition does not have to
   hold every relation's body.
4. As a developer, I want the relation property's type to be what identifies it as a relation, so that forgetting an
   attribute is not a mistake I can make.
5. As a developer, I want to still be allowed to write `[Relation]` on the property, so that the attribute stays
   available where I want the intent spelled out.
6. As a developer, I want `[Relation]` on a property that is not a relation to fail the build, so that the attribute
   never says something untrue.
7. As a developer, I want a relation to one row stated by typing the property as the relation definition, so that
   cardinality is stated the way it always was.
8. As a developer, I want a relation to many rows stated by typing the property as a list of the relation definition,
   so that the declaration reads the way I think about it — a list of links, not a one-to-many.
9. As a developer, I want a relation to one row to be nullable, so that an outer join finding nothing has somewhere to
   put that.
10. As a developer, I want a relation definition whose declaring type argument is not the table the property sits on to
    fail the build, so that the join is always between the two tables I meant.
11. As a developer, I want a relation whose target is not a Table definition in the same compilation to fail the build,
    so that I do not meet it as a missing table at run time.

### Stating the joined columns

12. As a developer, I want to state each joined column as a pair of expressions rather than a pair of names, so that
    renaming a property cannot silently break a relation.
13. As a developer, I want a pair whose two sides hold different types to fail to compile, so that a mismatch is caught
    where I wrote it rather than by a build-time analyzer.
14. As a developer joining a nullable column to a non-nullable one, I want that to compile, so that the ordinary
    outer-join shape is not refused.
15. As a developer with a composite key, I want to state one pair per column, so that a composite relation reads no
    differently from a simple one.
16. As a developer, I want the order I write the pairs in to carry no meaning, so that reordering lines cannot change
    what a relation does.
17. As a developer declaring a relation to many rows, I want to state pairs without working out which table owns the
    foreign key, so that both directions read alike.
18. As a developer, I want to reach a row through a column I marked unique rather than only through its primary key, so
    that a natural key is a first-class way to relate.
19. As a developer whose relation to one row pairs against columns nothing claims unique, I want a build warning, so
    that I learn it may reach an arbitrary row out of several.
20. As a developer, I want that to be a warning rather than an error, so that a relation whose condition makes the
    pairing unique still builds.
21. As a developer, I want a key pair that is not a direct property reference on each side to fail the build, so that I
    do not write an expression the library cannot turn into a join.
22. As a developer, I want a relation stating no pairs to fail the build, so that I never register a cross join by
    omission.

### Conditions

23. As a developer whose table holds a kind column beside an identifier column, I want to declare one relation per
    kind, so that I reach the right table without adding a column per kind to my C#.
24. As a developer, I want each of those relations to resolve only rows whose kind matches, so that reaching through
    one never returns another kind's row.
25. As a developer, I want to write the condition as an ordinary C# expression over the two rows, so that the compiler
    checks it where I wrote it.
26. As a developer, I want to omit the condition entirely, so that an ordinary relation costs nothing extra.
27. As a developer, I want a condition touching a member that will not exist on the generated data type to fail the
    build on my own line, so that I never have to read an error about generated source.
28. As a developer, I want a condition calling something the **Query surface** cannot translate to be permitted at
    build time, so that the library does not refuse expressions it has no test for.
29. As a developer, I want to reach through another relation inside a condition, so that a condition can look at a
    joined row.
30. As a developer, I want the condition to narrow filtering as well as materializing, so that reaching through a
    relation in a predicate means the same thing as including it.
31. As a developer, I want one condition per relation, so that there is one place to read what narrows it — two
    conditions are one expression joined with `&&`.
32. As a developer whose kind values are enum members, I want to compare against the enum member itself, so that
    renaming a member is a compile error rather than a silently dead relation.
33. As a developer, I want the value to reach PostgreSQL as a literal inside the join, so that each kind gets its own
    query plan.
34. As a developer, I want a relation to be an outer join always, so that I cannot silently drop rows by choosing
    wrongly.

### Both directions

35. As a developer, I want the reverse direction — a target row reaching the links pointing at it — declared with the
    same class and the same kind of condition, so that neither direction is a special case.
36. As a developer, I want each direction declared on its own, so that declaring one never quietly creates the other.

### Multi-tenant schemas

37. As a developer on a multi-tenant schema, I want a relation pairing a tenancy column against something that is not
    a tenancy column to warn, so that a relation cannot quietly reach another tenant's rows.
38. As a developer, I want that check to look at both tables rather than only the one holding the primary key, so that
    it covers the direction it used to miss.
39. As a developer, I want a conditioned relation whose pairs include the tenancy column on both sides to produce no
    warning, so that the check does not fire on the shape it exists to permit.
40. As a developer whose target's whole primary key is the tenancy column, I want a relation pairing that one column
    plus a condition to work, so that a per-tenant singleton is reachable.

### Reading through a relation

41. As a developer, I want `Include` and `ThenInclude` to work across a conditioned relation exactly as across any
    other, so that materializing costs no new API.
42. As a developer, I want a relation to one row to fold into a single left join, so that including one costs no extra
    statement.
43. As a developer including several conditioned relations that share their pairs, I want each to resolve
    independently, so that I can ask a link row what it points at without knowing the kind first.
44. As a developer, I want the join never to widen into an "or both are null" alternative, so that my composite index
    stays usable.

### Mistakes and upgrading

45. As a developer, I want a broken relation to drop only that relation and still generate the table, so that one
    mistake does not bury itself under a wall of type errors.
46. As a developer with a conditioned relation and an unconditioned one over the same pairs, I want a warning, so that
    a forgotten condition does not silently return every kind.
47. As a developer upgrading, I want the old declaration shown next to the new one in the README, so that I can convert
    a Table definition without guessing.
48. As a developer upgrading, I want the old attribute-argument form to stop compiling rather than keep working
    differently, so that I am never half-migrated without knowing it.

### What stays the developer's own

49. As a developer, I want the library to keep creating no schema, so that my migrations remain the only thing that
    changes my database.
50. As a developer, I want to keep my own generated column and foreign key per kind, so that the database still refuses
    a link pointing at a row that does not exist.
51. As a developer, I want those per-kind columns never to appear in my C#, so that I model the shape once instead of
    three times over.

## Implementation Decisions

### The public runtime surface

- A new abstract class `RelationDefinition<TDeclaring, TTarget>` ships in the library package. Its two type arguments
  are the two Table definitions. `TDeclaring` and `TTarget` are the names to use — the resolver and the diagnostics
  already speak that way, and left/right do not survive the relation-to-many direction, where the declaring table sits
  on the right of the join.
- It exposes two overridable members and nothing else for now. `Keys` is abstract, because a relation with no pairs is
  a cross join and there is no sensible default. `Condition` is virtual and defaults to no condition, so a definition
  class stays valid as the base type gains members later. Being a class is the point: further members are additive.
- `Keys` returns `IReadOnlyList<RelationKey>`. Pairs are built by a `protected static Key(…)` on the base type.
  `RelationKey` is an opaque value; nothing reads it at run time.
- `Key(…)` is generic over the column's type, so a pair whose two sides hold different types does not compile. One
  further overload takes a nullable left side against a non-nullable right, which is the ordinary outer-join case and
  the reason the current type check strips nullability. Whether a nullable right side needs an overload too is the one
  open question below.
- `Condition` is `Expression<Func<TDeclaring, TTarget, bool>>`, not `Func<…>`. The generator lifts either, and the
  expression type is what states honestly that this is a tree to be read rather than a delegate to be called.
- These types are declarations, not configuration. Nothing instantiates a relation definition and nothing calls its
  members. This is why a `private` nested class works: a syntax reader needs no access, where generated code calling
  into it would. It is also why the library does not require the class to be nested at all — nesting keeps a relation
  next to the table it belongs to, which is worth doing, but the type arguments say which tables are involved wherever
  the class lives.
- `RelationAttribute` loses its constructor parameters and becomes a bare marker that the generator accepts and
  ignores.

### What the generator does

- The relation-property split stops being attribute-driven and becomes type-driven. A property whose type derives from
  `RelationDefinition<,>`, or is one of the already-supported collection types over such a type, is a relation
  property. Everything else is a column candidate. `HashSet<T>` stays unsupported, as it is today.
- Cardinality still comes from the property's type: the definition class alone is a relation to one row, a collection
  of it is a relation to many. The generator gains one unwrapping step, from the definition class to `TTarget`.
- The target and the declaring table come from the type arguments rather than from the property type and the enclosing
  class. The target must still be a `[Table]` class in the same compilation, and the declaring type argument must be
  the class the relation property is declared on.
- Key pairs are read from the `Keys` override's syntax. Each pair must be a direct property reference on both sides,
  resolving to a mapped column of the respective table. Because a pair states both sides itself, the resolver no
  longer decides which table owns the foreign key, and **Key order** no longer governs relation matching. The order the
  pairs are written in carries no meaning either — they are joined with `&&`, so reordering them cannot change what a
  relation does.
- A relation to one row must pair against a set of target columns that contains something the target claims unique —
  its primary key, or a column marked `[Unique]`. A superset of a unique set is still unique and passes. This is a
  claim, not a check, exactly like every other claim in a Table definition.
- The condition's body is lifted from the override's syntax and inlined into the join condition alongside the pairs,
  joined with `&&`. The lift rewrites the two parameters from Table definition types to generated data types; member
  names are identical between the two, so the body otherwise copies verbatim. A constant in the body stays a constant,
  so it reaches PostgreSQL as a literal in the join rather than as a parameter, and each relation gets its own plan.
- The condition's body is policed at its parameters only. A member touched on either parameter must exist on that
  table's generated data type — a mapped column or another relation property. Everything else passes through
  untouched, including calls the Query surface may refuse at run time. The narrow refusal exists because the
  alternative failure is a compile error inside generated source with no line in the developer's own code to fix.
- Emission is unchanged in shape. Every relation now registers through the provider's predicate-based association
  overload, which composite keys already use, always with an outer join. The key-expression overloads on the public
  mapping builder become unreachable from generated code and are removed.
- The generated data type is unchanged: a relation still appears there as the target's generated data type, or a list
  of it. `Include`, `ThenInclude` and the filtered include overload are untouched, and so are their costs — a relation
  to one row folds into a left join, a relation to many rows adds one statement per level.

### Diagnostics

Three are retired, and their ids are not reused:

- Foreign-key property not found (`PGSQL0012`) and foreign-key type cannot match the primary key (`PGSQL0013`)
  disappear into the compiler, because the pairs are now expressions the compiler checks.
- The foreign-key arity rule (`PGSQL0019`) has no fixed arity left to check, since a relation states its pairs
  explicitly rather than matching a count against the target's primary key. What it protected — a relation to one row
  reaching more than one — is now the uniqueness warning below.

Three are reshaped:

- Relation target is not a Table definition (`PGSQL0014`) stays, reading the target from the type argument. No generic
  constraint can express "is a `[Table]` class in this compilation".
- Relation to one row must be nullable (`PGSQL0015`) stays, applying to the relation property.
- The relation could reach across tenants warning (`PGSQL0027`) becomes pair-based and direction-free: a tenancy column
  appearing on either side of the relation must be paired with a tenancy column on the other side, and a tenancy
  column paired with nothing warns. This is stricter than the positional rule it replaces and it now covers the
  declaring side too.

New ids continue from `PGSQL0028`, in this order:

| Rule | Severity | Trigger |
| --- | --- | --- |
| Relation declaring table mismatch | Error | The `TDeclaring` type argument is not the Table definition the relation property is declared on |
| Relation states no keys | Error | The `Keys` override yields no pairs |
| Relation key is not a column reference | Error | Either side of a pair is not a direct reference to a mapped column of that table |
| Relation to one row may reach several | Warning | The target-side columns contain nothing the target claims unique |
| Relation condition cannot be carried | Error | The condition touches a member on either parameter that has no counterpart on that table's generated data type |
| Relation attribute on a non-relation property | Error | `[Relation]` sits on a property whose type is not a relation definition |
| Relation may resolve every kind | Warning | One table declares a relation with a condition and another with the same key pairs and no condition |

Blast radius is unchanged from ADR 0005: a relation-level error drops that relation and nothing else, so the table
still generates and the developer reads one message rather than a wall of type-not-found errors. Every new descriptor
gets a row in `AnalyzerReleases.Unshipped.md` with its title verbatim.

### Documentation and versioning

- An ADR records this, superseding the declaration half of ADR 0005 and absorbing ADR 0006's composite-key story,
  which pairs make unremarkable. ADR 0005 stays in place pointing forward, the pattern it used on ADR 0004 itself.
- `CONTEXT.md` is already updated: **Relation** rewritten, **Relation definition**, **Relation key** and **Relation
  condition** added, **Relation property** and **Key order** corrected.
- The library README's relation section is rewritten, with the old declaration shown beside the new one so an upgrade
  can be done mechanically. The diagnostics table gains the new rows and loses the retired ones.
- This breaks a published package. Pre-1.0 that is a MINOR bump: `0.36.0` to `0.37.0`.
- The cost is on the record: every relation now takes about five lines where a plain one takes two today. That is the
  price of a single mechanism, chosen deliberately over two.

## Testing Decisions

A good test here asserts what someone outside the library can observe, and nothing else. For the generator that means
which diagnostics come out of a compilation and what the emitted registration says — never which internal model type
held what. For the runtime it means which rows come back and what SQL ran. A test that reaches into the resolver's
intermediate shapes will have to be rewritten by the next change to it and will not have caught anything.

Two existing seams cover this feature, and no new one is needed.

**The generator harness.** `GeneratorHarness.RunGenerator(source)` compiles hand-written source in memory against
runtime stubs and returns the diagnostics and the generated sources. Everything on the declaration side is tested
here: the type-driven relation split, cardinality unwrapping, key-pair reading, condition lifting, every new and
reshaped diagnostic, and the exact text of the emitted association call. Prior art is the three existing generator
test classes covering relations, composite keys and tenancy; the new tests follow their shape, including the companion
"a well-formed declaration reports nothing" test and the "emitted source compiles" test that each feature area carries.

The stubs in that harness must gain `RelationDefinition<,>`, `RelationKey` and `Key(…)`, and `RelationAttribute` must
lose its constructor parameters there too. This is worth calling out because a stub that drifts from the real type
makes analyzer tests pass on a shape that would not compile for a real consumer. The existing "emitted source
compiles" assertion is the guard, and it should be kept on every new test class.

**The integration suite.** Tests against a real PostgreSQL container through `TestBase` cover what the generator
cannot: that a conditioned relation returns the rows it should and no others, in both directions; that a relation to
one row still produces a single left join and a relation to many rows still costs one statement per level; that the
join carries plain column equality plus the condition's literal, with no `IS NULL` widening; and that including
several conditioned relations sharing their pairs resolves each one independently. Prior art is the existing relation
and composite-key integration tests, one of which already pins SQL text and is the model for the join-shape
assertions.

Two fixtures need adding to that suite rather than only converting: a link table carrying a kind column and an
identifier, with conditioned relations to two different targets in both directions, and a per-tenant singleton whose
whole primary key is the tenancy column, reached by a condition alone.

**The OData suite** needs no new tests. Its fixtures convert to the new declaration form and the existing conformance
and regression tests must keep passing unchanged, which is the check that the Query surface's behaviour did not move.

All 45 existing relation declarations across the three test projects convert to the new form. That conversion is the
broadest evidence the feature works, because those declarations are already exercised end to end.

## Out of Scope

- **Emitting any DDL.** The library creates no schema and this does not change that. The per-kind generated column and
  its foreign key stay in hand-written migrations.
- **Enforcing that a link points at a row that exists.** PostgreSQL cannot express a foreign key whose target table
  depends on another column's value, so a developer who wants that enforcement keeps something physical per kind
  however this turns out.
- **Scaffolding the migration from the `db` CLI.** A reasonable idea, and a separate one.
- **A relation reaching several kinds at once.** One condition per relation. A relation reaching two kinds cannot say
  which kind a row was, and permitting a second value later is additive.
- **Letting the condition replace the key pairs.** Pairs stay required. Without them the tenancy check has no columns
  to pair and would stop warning rather than report anything, which is worse than not having the feature.
- **An inner-join option on a relation definition.** The provider already collapses to an inner join when a predicate
  lands on the far side, so the option would buy only a way to drop rows silently.
- **Pairing the two directions.** Relations stay one-directional. Declaring one never implies the other.
- **Verifying anything against the real database.** No relation, key pair, uniqueness claim or condition is checked
  against a live schema.
- **Evaluating a relation definition at run time.** The generator reads it from source. Nothing constructs one.
- **Ordering or paging inside a relation to many rows.** The provider's associations do not carry it.

## Settled: the nullable target side

`Key(…)` needs a second overload for the ordinary case of a nullable column joining a non-nullable one, and the spec
assumes that only ever happens on the left. On the target side a primary-key column can never be nullable, so the
assumption holds for every relation possible today. It stops holding the moment a relation may pair against a
`[Unique]` column instead, because nothing in the library refuses a nullable one — the nullability rules cover primary
keys and tenancy columns only.

**Decision: the build refuses a relation pairing against a column that is both `[Unique]` and nullable.** A nullable
unique column matches at most one row but may match none for reasons the relation cannot see, so the refusal is right
on its own merits. `Key(…)` therefore keeps exactly two overloads — matching types, and a nullable left against a
non-nullable right. There is no third overload for a nullable right side.

This adds one diagnostic to the New-ids table below, continuing the same sequence:

| Rule | Severity | Trigger |
| --- | --- | --- |
| Relation pairs against a nullable unique column | Error | A relation pairs against a target column marked `[Unique]` that is nullable |

It is a new refusal on a shape that builds today, and it is deliberate.

## Further Notes

The kind column really is a discriminator, and `CONTEXT.md` deliberately does not call it one. The word is listed under
_Avoid_ on the **Tenancy column** entry so that a reader never confuses the two, and the general form this feature took
does not earn the word back — a **Relation condition** is any expression, and selecting on a kind is only its
motivating use.

The driving consumer's side of this is tracked in the `mvdmio-suite` repository as
`.agents/ideas/compliance-table-definitions-and-typed-links.md`, which is proceeding without this feature. It is not
blocked on the outcome here. This removes modelling cost from that work rather than unblocking it.

The idea this spec came from, `.agents/ideas/discriminated-relations.md`, asked a narrower question: whether a relation
could read a discriminator column. The grilling session widened it twice — first to any condition over the two rows,
then to a class-based declaration replacing the attribute mechanism entirely — on the grounds that one mechanism beats
two. That idea file is superseded by this spec.
