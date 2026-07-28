# Composite primary keys in table definitions, relations and the query surface

Status: ready-for-agent

## Problem Statement

A consuming application cannot declare a single **Table definition** against its schema.
Fifty-three of its fifty-nine keyed tables have a two-column primary key, every one of
them shaped `(account_id, <entity>_id)`, and the generator rejects a **Table definition**
with anything other than exactly one primary-key property — abandoning the whole table, so
no data type, no repository, no query mapping and no relations are generated for it.

That composite key is not incidental to the consumer. It *is* their tenancy guarantee:
`account_id` is part of every key and every foreign key, so no query can reach another
tenant's row through a **Relation** by accident. Demoting it to an ordinary filtered column
to satisfy the generator would trade a structural guarantee for a call-site convention, and
the consumer has ruled that out.

The consequence is that the entire **Query surface** — the `IQueryable<T>` the library
exists to expose, and the OData front-end built over it — is unreachable for the application
that asked for it.

Two claims in [ADR 0005](../../docs/adr/0005-table-relations-on-relation-properties.md)
stand in the way and both need revisiting. It records that "exactly one non-composite
primary key per Table definition is already enforced", which is the rule to lift, and it
leans on that rule to conclude that "the provider's key-based association API, which does not
support composite keys, is sufficient" — which is only true of the provider's *key-expression*
overloads, and was never true of the provider as a whole.

The consumer additionally needs a **Relation** declarable against a Postgres stored generated
column, because that is how it intends to resolve a junction that is polymorphic across
roughly thirteen tables: a per-kind generated column that is non-null only for its own kind,
carrying a composite foreign key.

## Solution

A **Table definition** may mark two or more properties as its primary key. Their source
declaration order is the **Key order**, and that order is public contract: it fixes the
parameter order of the generated primary-key lookup and the order a **Relation**'s foreign-key
properties are matched against the target's key.

A **Relation property** may name more than one foreign-key property. The far end stays what it
has always been — the target's declared primary key — so a declaration still states only the
near side, and the two sides are paired positionally. Filtering and ordering through such a
**Relation** costs no new API, exactly as for a single-column one, and opt-in eager loading of
it keeps ADR 0005's execution-time include translation untouched.

Every repository gains a single, uniformly named primary-key lookup and delete taking one
parameter per key member, replacing today's per-property names. A reader who sees
`GetByPrimaryKeyAsync` knows immediately where it came from and never has to discover what it
is called on a different repository.

The consumer's polymorphic junction needs nothing from the library beyond the above. A stored
generated column is an ordinary column that happens to be database-computed, already expressible
with the existing generated-column marker, and a **Relation** against it is an ordinary
**Relation**.

## User Stories

1. As a consumer developer, I want to mark two or more properties as the primary key of a **Table definition**, so that I can declare a table whose key is composite.
2. As a consumer developer, I want the primary key's **Key order** to be the order I declared the properties in, so that I do not have to state an ordinal for something the file already shows.
3. As a consumer developer, I want a **Table definition** with no primary-key property at all to stay an error, so that a forgotten marker does not silently generate a repository with no way to address a row.
4. As a consumer developer, I want a nullable primary-key property to be an error, so that I cannot declare a key the database would reject.
5. As a consumer developer, I want a composite primary key to generate a data type, a create command, an update command, a repository and a repository interface exactly as a single-column key does, so that nothing about my table is second class.
6. As a consumer developer, I want one uniformly named primary-key lookup on every repository, so that I never have to look up what the lookup is called for a given table.
7. As a consumer developer, I want that lookup to take one parameter per key member in **Key order**, so that the call site reads in the same order as the declaration.
8. As a consumer developer, I want a uniformly named primary-key delete alongside it, so that the two mirror each other.
9. As a consumer developer, I want the generated update to address a row by every key member, so that an update against a composite-key table affects exactly one row.
10. As a consumer developer, I want `[Unique]` columns to keep their own per-property lookup and delete names, so that two unique lookups on one table stay distinguishable.
11. As a consumer developer, I want a key member that the database computes to be markable as generated and excluded from the create command, so that a table whose key is part-supplied and part-generated is expressible.
12. As a consumer developer, I want to name more than one foreign-key property on a **Relation property**, so that I can declare a relation whose foreign key is multi-column.
13. As a consumer developer, I want a single-column **Relation** declaration to keep compiling and behaving exactly as it does today, so that adopting this release does not force me to revisit relations that already work.
14. As a consumer developer, I want a **Relation** whose foreign key shares a column with the declaring table's own primary key, so that my tenancy column can appear on both sides of the join.
15. As a consumer developer, I want to declare a **Relation** against a stored generated column, so that I can resolve a polymorphic junction through a per-kind column.
16. As a consumer developer, I want a **Relation** whose foreign key is not part of the declaring table's own primary key, so that a junction with a four-column key can still point at a two-column key.
17. As a consumer developer, I want an error when I name a different number of foreign-key properties than the target's primary key has members, so that a half-declared composite relation cannot reach run time.
18. As a consumer developer, I want an error when a foreign-key property's type cannot match the key member it is paired with, naming the position as well as both properties, so that I can find which pair I got wrong.
19. As a consumer developer, I want an invalid **Relation** to be reported without abandoning the table, so that one bad declaration does not bury its own diagnostic under a wall of type-not-found errors.
20. As a consumer developer, I want filtering through a composite **Relation** to need no new API, so that a predicate reaching through it reads the same as through a single-column one.
21. As a consumer developer, I want ordering through a composite **Relation** to work, so that I can sort by a column on the related table.
22. As a consumer developer, I want a to-many composite **Relation** usable in an existence predicate, so that I can filter parents by something about their children.
23. As a consumer developer, I want opt-in eager loading of a composite **Relation**, so that I can materialize related rows without a second round trip per parent.
24. As a consumer developer, I want eager loading of a composite **Relation** to work two levels deep, so that a chain of relations materializes in one composition.
25. As a consumer developer, I want the filtered eager-loading overload to work over a composite **Relation**, so that I can narrow a materialized collection.
26. As a consumer developer, I want a query composed before a transaction opens to still execute inside it when it includes a composite **Relation**, so that this release does not weaken an already-tested guarantee.
27. As a consumer developer, I want exception translation, SQL diagnostics and the disposed-connection error to keep working across a composite **Relation**, so that the decorator stays in the chain.
28. As a consumer developer, I want the generated join for a composite **Relation** to constrain every key column, so that the database can use a composite index on it.
29. As a consumer developer, I want a nullable foreign key against a non-nullable key member to render plain equality, so that a relation against a generated column does not silently lose its index.
30. As a consumer developer, I want the query surface to keep treating a composite **Relation** as a nullable join, so that a relation I got wrong does not silently drop rows.
31. As a consumer developer with an OData front-end, I want `$filter` over a composite-key entity to work, so that my endpoint answers the queries it answers today for single-key tables.
32. As a consumer developer with an OData front-end, I want `$orderby`, `$top`, `$skip` and `$count` over a composite-key entity to work, so that paging and totals behave.
33. As a consumer developer with an OData front-end, I want `$select` over a composite-key entity to work, understanding that the front-end appends every key column rather than one, so that a narrowed projection is not a surprise.
34. As a consumer developer with an OData front-end, I want `$filter` through a navigation property between two composite-key entities to reach the database with both key columns in the join, so that navigation-path filtering is not quietly wrong.
35. As a consumer developer with an OData front-end, I want the front-end's stable-paging token to work over a composite key, so that server-driven paging is available.
36. As a consumer developer with an OData front-end, I want the null-propagation warning to survive this change, so that the one setting I must get right stays discoverable.
37. As a consumer developer with an OData front-end, I want the existing null-propagation regression coverage to keep passing over composite keys, so that the one setting I must get right is still guarded after this change.
38. As a library maintainer, I want the composite association registered through a provider API whose keys are compile-checked expressions, so that a renamed property is a build error rather than a first-query failure.
39. As a library maintainer, I want the provider's anonymous-type key form to be unreachable from generated code, so that we cannot emit a mapping that registers wrong and fails only at run time.
40. As a library maintainer, I want each key member emitted as a primary key in the query mapping, so that split eager loads join the parent table directly rather than through a derived deduplicating query.
41. As a library maintainer, I want the single-column relation registration path left exactly as it is, so that this change carries no regression risk for relations that already ship.
42. As a library maintainer, I want the composite overload added to the public mapping builder rather than hidden, so that generated code in a consumer's assembly can call it.
43. As a library maintainer, I want the outer-join default kept internal to the builder, so that nobody can choose an inner join by mistake.
44. As a library maintainer, I want a generated-name collision against the new fixed lookup name diagnosed, so that a unique column that happens to be named after it does not emit the same method twice.
45. As a library maintainer, I want ADR 0005's two false claims corrected in place, so that the record states today's decision rather than a superseded one.
46. As a library maintainer, I want a new ADR recording why the composite association uses a predicate rather than the key API and why a nullable key member is refused, so that a future reader does not undo either.
47. As a library maintainer, I want the OData conformance suite to pin composite-key behaviour, so that the front-end claim stays a regression-guarded artifact rather than prose.
48. As a library maintainer, I want the glossary to describe a primary key as one or more columns, so that the domain language matches what the code allows.
49. As an agent implementing this, I want the diagnostics enumerated with their IDs and what each abandons, so that I do not invent a numbering or a severity.
50. As an agent implementing this, I want the provider facts that were established by executing against a real database, so that I do not re-derive them or trust a plausible-looking alternative.
51. As a consumer developer with an OData front-end, I want `$expand` of a to-one navigation property between composite-key entities to work, so that I can return a related row with its parent.
52. As a consumer developer with an OData front-end, I want `$expand` of a to-many navigation property between composite-key entities to work, so that I can return a related collection.
53. As a consumer developer with an OData front-end, I want two-level `$expand` over composite keys to work, so that a chain of navigation properties materializes in one request.
54. As a library maintainer, I want `$expand` over composite keys guarded by the conformance suite, so that the newest front-end feature is not shipped untested against the newest key shape.

## Implementation Decisions

### Declaring a composite primary key

- The primary-key attribute is **unchanged** — no constructor, no ordinal argument. Two or more properties may carry it.
- **Key order** is source declaration order. An ordinal argument was considered and rejected: the provider treats key order as cosmetic (it changes only the ordering of predicates in a generated `WHERE` clause, and key metadata is not consumed by the read path at all), so the ordinal's only audience would be the generated parameter order, which declaration order already gives — at the cost of four malformed-ordinal cases to diagnose.
- The parsed table model changes from carrying one primary-key property to carrying an ordered collection of them. Everything that derives from it follows: the update command's property order, the update statement's `WHERE`, and the query mapping's per-member key flag (which already reads a per-property flag and needs no emitter change).
- The create command is unaffected in principle — it already excludes generated columns per property, so a key that is part caller-supplied and part database-computed works without special handling.

### Diagnostics

The two new IDs continue from the highest currently allocated one, which is `PGSQL0018`.
Relation-level problems drop only the relation, per ADR 0005; key-level problems abandon the
table, per the existing precedent, because a malformed key leaves every generated signature
undefined rather than one relation. All are errors; none are warnings.

| Concern | ID | Disposition |
| --- | --- | --- |
| Zero primary-key properties | `PGSQL0004` | Keeps its ID; title and message reworded from "exactly one" to "at least one". Abandons the table. |
| Nullable primary-key property | `PGSQL0020` (new) | Abandons the table. |
| Relation foreign-key arity does not match the target's key arity | `PGSQL0019` (new) | Drops the relation. |
| Foreign-key property type cannot match the key member it is paired with | `PGSQL0013` | Message extended to name the **position** alongside both property names and types. Drops the relation. |
| Generated method name collision against the new fixed lookup name | `PGSQL0010` | Scope extended. |
| Duplicate generated lookup method name | `PGSQL0006` | Scope narrows to unique columns only; message unchanged. |

Both new IDs need their release-tracking entry alongside the existing ones, and the reworded
`PGSQL0004` entry updating.

Refusing a nullable key member is load-bearing rather than tidy — see *Why a nullable key
member is refused* below.

### The generated repository surface

- The primary key produces **one** lookup and **one** delete, both fixed-named after the
  primary key rather than after its properties, taking one parameter per key member in **Key
  order**. This applies to single-column keys too: uniformity across repositories was the whole
  reason for the name, and it only holds if it is universal.
- This **renames a method on every existing repository**. Accepted: the package is pre-1.0, and
  under the project's beta versioning rule a breaking change is a minor bump.
- `[Unique]` columns keep their per-property lookup and delete names, because the property name
  is the only thing distinguishing two unique lookups on one table.
- The internal notion of "lookup properties" stops meaning *primary key plus unique columns* and
  narrows to *unique columns*; the key gets its own emission path.
- The update statement addresses the row by every key member.
- The Dapper surface is otherwise untouched, and relations still do not reach it at all.

### Declaring a composite relation

- The relation attribute's single foreign-key-name parameter is **replaced** by a variadic one,
  and its single-name property by a collection. A single-argument declaration keeps compiling
  unchanged; this is binary-breaking and source-compatible.
- The far end of a **Relation** remains the target's declared primary key. Naming the target's
  columns explicitly was considered and rejected as unmotivated scope — nothing in the driving
  schema needs a relation to a non-primary unique key, and it would double what every
  declaration states.
- Pairing is **positional**: the *n*th named foreign-key property against the *n*th key member.
  Explicit pairing syntax was rejected because it restates the target's key order and buys no
  safety — with two same-typed columns a transposed pair is silently wrong under either scheme.
- Foreign-key properties are resolved against the declaring side's mapped columns for a to-one
  relation and the target's for a to-many, exactly as today. A generated column is an ordinary
  mapped column and is eligible.
- A foreign key is **not** required to be part of the declaring table's own primary key, nor a
  subset of it. The driving case is a junction with a four-column key whose relation to another
  table is keyed on the tenancy column plus a generated column that is in no key at all.
- The include operators, their marker interface, the library's own include expression nodes and
  the execution-time translation are all **unchanged**. Eager loading of a composite relation goes
  through the same path, which is what preserves the already-tested guarantees that a query
  composed before a transaction opens still executes inside it, and that exception translation,
  SQL diagnostics and the disposed-connection error still apply across one.

### Registering the composite association with the provider

The provider offers three forms that carry composite keys. All three were executed against
PostgreSQL 18 in a prototype; all three render identical SQL and all three support eager
loading at every level (to-one, to-many split query, two-level chained, and filtered).

- **Chosen: the predicate form**, exposed as two new overloads on the public mapping builder —
  one per cardinality, one type parameter each, the predicate second, near side first. The
  outer-join default stays internal to the builder, per ADR 0005.
- **Rejected: the comma-separated key-name form.** It carries member *names* as strings; the
  prototype showed a wrong name registers without complaint and fails only at first query, and
  that a mismatched key count throws on one entity while silently dropping the association from
  the other. The generator would emit names it had just validated, but the mapping builder is
  public API a consumer calls by hand, where an expression is compile-checked and rename-safe.
- **Rejected and actively guarded against: the key-expression form with a composite key.** The
  provider's key generics are unconstrained, so an anonymous type or tuple compiles, registers
  as a *single* key literally named after the constructor, and fails only at first query with a
  coercion error naming the two entity types. Tuple literals do not compile at all in an
  expression tree. This shape must be unreachable from generated code, and a generator test
  asserts it.
- The **single-column path is left exactly as it is**. Unifying everything onto the predicate
  form was considered and rejected as regression risk for no gain.

The emitted shape, from the prototype, because it encodes the decision more precisely than
prose:

```csharp
.Relation<global::Consumer.FindingData>(x => x.Finding, (x, y) => x.AccountId == y.AccountId && x.FindingId == y.FindingId)
```

Each key member continues to be emitted with the key flag on its column registration. Key
metadata is not required by the read path, but the prototype established that its absence makes
a split eager load wrap the parent in a deduplicating derived query instead of joining the
parent table directly, so emitting it is a free plan improvement.

### Why a nullable key member is refused

The prototype established a sharp cliff. When the provider believes a key column is nullable on
**both** sides of a join, its null-comparison mode — kept deliberately by
[ADR 0004](../../docs/adr/0004-linq2db-as-the-queryable-provider.md) because it matches C#
semantics — widens the join condition with an "or both are null" alternative:

```sql
ON x.account_id = y.account_id AND (x.finding_id = y.finding_id OR x.finding_id IS NULL AND y.finding_id IS NULL)
```

Measured against PostgreSQL 18 on two fifty-thousand-row tables with a matching two-column btree
index on each side, that widening demotes the second column out of the index condition into a
filter: index searches rise from 250 to 50,000, shared buffers from 1,516 to 25,116,417, and
runtime 232x on a nested loop; on the default planner's hash join the second column becomes a
join filter removing 24,975,000 rows, costing 54x. Row counts are identical in every case, so
the widening is pure cost.

There is no local suppression. Marking the property not-null in the mapping collapses the clause
but corrupts predicate translation — an equality against null becomes an always-false constant
and an inequality against null loses its `WHERE` entirely, both silently. The only real knob is
the provider's global null-comparison mode, which is the semantics ADR 0004 chose on purpose.

Because the far end of every **Relation** is always the target's primary key, **refusing a
nullable key member makes the widened form unreachable by construction.** That is why this is a
key-level error rather than a warning on the relation. The prototype also confirmed the
converse: a nullable foreign key against a non-nullable key member — which is exactly the
generated-column case, since a per-kind generated column is null for every other kind — renders
plain equality in all three registration forms. The consumer's polymorphic junction is therefore
clean without any special handling.

### Polymorphic junctions

The library learns **nothing** about discriminators. A junction polymorphic across roughly
thirteen tables declares roughly thirteen ordinary to-one relation properties, each keyed on the
tenancy column plus its own per-kind stored generated column, which is marked with the existing
generated-column attribute so it is excluded from create and update. A discriminator-aware
relation form was considered and rejected as a large feature for something the database already
expresses.

Nothing in the library verifies that the generated column exists, that its definition is what
the consumer thinks, or that a foreign key backs it — the same trade the library already makes
for column names and for relations generally.

### Records to update

- **New ADR**, numbered 0006 with the slug `composite-primary-keys`, covering: lifting the
  single-key rule, why the composite association uses the predicate form rather than the key API,
  why a nullable key member is refused, and the fixed-named primary-key lookup as a breaking
  rename. The number and slug are already forward-referenced by the tenancy idea, so keep them.
- **ADR 0005**, corrected in place with no superseded banner: the sentence asserting that exactly
  one non-composite primary key is enforced, and the claim that the provider's key-based
  association API does not support composite keys — which is true only of its key-expression
  overloads. ADR 0005 keeps stating today's decision about *how relations are declared*, which
  this does not change.
- **README** and the centralised package version (minor bump from the current patch level).

No ADR 0004 or README change is needed for the front-end crash described under *Further Notes* —
it was investigated during this session and found not to apply to this library.
- **Glossary**: already updated in this session — the table-definition entry now says "which of
  them form the primary key", the relation entries are pluralised, and a **Key order** term was
  added.

## Testing Decisions

A good test here asserts externally observable behaviour: a diagnostic the consumer sees, a
method the consumer can call, rows the database returns, or SQL the database receives. It does
not assert the shape of an internal model or the order of emitter calls. The one deliberate
exception is generated *source* assertions, which are the established way this repo pins
generator output and are the only place a never-emit-this-shape guard can live.

No new seams are needed. Three already exist at the right altitudes and each has prior art.

**Generator seam** — the existing analyzer test project drives the generator over an in-memory
compilation and asserts diagnostics and emitted source. Prior art: the existing valid-table test
asserting the emitted CRUD types and the column registration line, and the existing relation
diagnostic tests. Cover here:

- A composite-key table generates every type, with the lookup and delete taking one parameter per
  key member in declaration order.
- A single-column table generates the same fixed-named lookup, proving uniformity.
- Zero key properties, a nullable key property, relation arity mismatch, and a positional type
  mismatch each produce their diagnostic — and the relation ones leave the table's other output
  intact while the key ones do not.
- A unique column colliding with the fixed lookup name is diagnosed.
- The emitted composite registration uses the predicate form, and **never** an anonymous-type or
  tuple key expression. This is the defensive guard against the prototype's silent trap.
- The zero-key diagnostic is backfilled while its message is being reworded; it has no test today.

**Behaviour seam** — the main integration suite's per-test base gives a real connection inside a
rolled-back transaction against a containerised PostgreSQL 18, with committed fixture DDL. Prior
art: the existing relation tests, which already cover navigation-path filtering, null-foreign-key
outer-join semantics, two-level eager loading, filtered eager loading, self-reference, junction
traversal, and hierarchy filter scoping. Add a **new, separate** fixture table set rather than
touching the existing author/book/tag set, which pins the single-key path:

- Two tables with two-column keys sharing a column, so the shared-column case is real.
- A to-one relation whose foreign key shares a column with the declaring table's own key, plus
  its to-many mirror.
- **One** junction with a four-column key, carrying **one** stored generated column with a
  composite foreign key and an ordinary relation declared against it. Since the library learns
  nothing about polymorphism, one generated-column relation is the proof for all thirteen kinds;
  building thirteen would test the consumer's schema, not this library.

Cover the generated lookup, delete and update against a composite key; filtering, ordering and
existence predicates through a composite relation; eager loading to-one, to-many, two-level and
filtered; the relation against the generated column; and — as regression cover for the plan
cliff — that the join reaching a composite relation constrains every key column. The
already-tested guarantee that a query composed before a transaction opens still runs inside it
should be exercised once through a composite relation.

**Front-end seam** — the OData conformance project drives the front-end's query-option
application in process, with no web host, over a real database, and renders the resulting SQL.
Prior art is now substantial: its query-option, filter-function, relation-navigation, expand and
relation-misconfiguration tests, all over a relation-bearing fixture whose two entity types have
**single-column** keys, with their EDM keys declared one property at a time. Add a composite-key
entity pair alongside that fixture — not replacing it, since it pins the single-key path — and
pin: `$filter`, `$orderby`, `$top`, `$skip`, `$count`, `$select` (which appends *every* key
column, and the test should say so rather than leave it a surprise), navigation-path `$filter` for
both cardinalities with both key columns asserted in the rendered join, and the stable-paging
token's lexicographic ladder. Also pin `$expand` over the composite pair — to-one, to-many, and two
levels — since `$expand` now ships and nothing else guards it against a composite key.

All of the front-end cases above were already executed green in a prototype, so this seam is
writing down a regression guard rather than discovering behaviour. The EDM model builder accepts a
composite key either as chained per-property calls or as a single anonymous-type selector; the
existing fixture's per-property style extends to composite keys unchanged.

The existing relation-misconfiguration coverage should be extended to the composite pair too, so
the null-propagation symptom stays guarded on both key shapes.

The repo's finishing sequence applies: format, then build, then test, run sequentially and never
in parallel. The integration and conformance suites each need Docker.

## Out of Scope

- **Declaring and enforcing a tenancy column.** The property the consumer is protecting is that
  the tenancy column cannot be omitted, and a composite key gives that structurally *inside a
  join* but not at the root of a query. Making omission a compile error is a good idea and a
  larger surface than composite keys, so it is filed as its own idea rather than folded in.
- **Enabling or extending `$expand` itself.** It already ships. Only its behaviour over a
  composite key is added here — see *Testing Decisions* and *Further Notes*.
- **Relations reaching the Dapper surface.** Unchanged from ADR 0005: its SQL is baked at
  generation time with flat aliasing, and joined reads there are new machinery for no stated need.
- **Relations to a non-primary unique key.** No declaration names the target's columns; the far
  end stays the target's primary key.
- **Discriminator-aware relations.** The database already expresses this with a generated column.
- **Composite unique constraints.** `[Unique]` stays per-column.
- **Verifying a relation, a key or a generated column against the real schema.** The table
  attribute has never emitted DDL and migrations stay hand-written; a declaration naming the wrong
  column type-checks, registers, and is wrong at run time. Same trade as today.
- **Changing the provider's null-comparison mode.** Settled by ADR 0004 and specification-correct.
- **Carrying column nullability into the query mapping generally.** A separate idea, whose
  open question about whether the sargability loss is measurable was answered by this session's
  measurements and updated there.

## Further Notes

**Why `$expand` is in this spec's front-end scope.** It was originally deferred to the separate
spec that owned enabling `$expand` at all. That work landed and merged while this spec was being
drafted and its spec was consumed and deleted, leaving nowhere for the deferral to point, so the
coverage was pulled in here. The fixture pair, the test base and the seam all have to be built for
the other query options regardless, so the marginal cost is writing the cases — and a prototype
already executed to-one, to-many and two-level `$expand` green over composite keys.

**A front-end crash that does not apply to this library — investigated and closed.** A prototype
found that the *provider's own* eager-loading operator, composed before the OData front-end
applies its query options with null propagation left at its default, is an uncatchable stack
overflow: process exit, infinite recursion inside the front-end's expression builder. That is
strictly worse than the silently-empty-collection symptom both ADRs document.

This library is **not** exposed. It records includes as its own expression nodes and translates
them at execution time, so the front-end never sees the provider's operator, and it never exposes
a provider type a consumer could hold instead. Confirmed by executing the conformance suite's
relation misconfiguration tests, which compose an include through the library's own path with null
propagation at its default: they pass, asserting empty collections, and the process does not
crash. Nothing to document and nothing to fix — recorded here only so the finding is not
rediscovered and mistaken for a defect.

**Provider facts established by execution, not from memory.** Against the pinned provider version
and PostgreSQL 18: composite associations work through the comma-separated-key, predicate and
query-expression forms, all rendering identical SQL, all supporting eager loading to-one, to-many
split, two-level chained and filtered. Multiple key flags register correctly. Key order affects
only predicate ordering in generated CRUD statements. The provider has no concept of a generated
column — from its side it is an ordinary column, usable as an association key, a key member, a
filter target and a projection. The front-end's model builder accepts a composite entity key via
chained key calls or an anonymous type, and across fifty-two executed cases nothing in its
query-option application requires a single-property key.

One separate provider gotcha, unrelated to keys but found in passing and worth knowing: eager
loading is silently dropped over a `Distinct()` parent — collections come back null with no error.

**Why the breaking rename is the right call at this size.** Renaming the primary-key lookup on
every repository is the largest consumer-visible cost here. It is justified only by uniformity:
the reason to prefer a fixed name over a derived one is that a reader never has to discover it,
and that reason evaporates if single-column tables keep the old name. The package is pre-1.0 and
the project's versioning rule makes this a minor bump.
