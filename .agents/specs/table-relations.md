# Table relations

Status: ready-for-agent

## Problem Statement

A **Table definition** describes one table in isolation. There is no way to say that one references another, so everything downstream of that gap is unavailable:

- A generated repository's **Query surface** can only ever see the columns of its own table. Filtering a child by a parent's column — the single most common cross-table need — is impossible through `Query()`, even though the LINQ provider underneath expresses joins perfectly well. ADR 0004 records this as a deliberate deferral: the provider was never the blocker, the missing relation model was.
- Reading a parent and its children in one composed query is impossible. Consumers drop to the Dapper surface and hand-write the SQL, which forfeits the runtime composability that made the query surface worth having.
- Nothing an OData or GraphQL front end does with `$expand` can be served, because there is no navigable shape to expand into.

The workaround today is to hand-write a joined query on the Dapper surface for every cross-table read, and to keep the join condition correct by hand in every one of them. The **Table definition** stops being the single source that every generated type derives from, precisely at the point where a schema has more than one table in it.

## Solution

A **Relation** — a declared correspondence between two **Table definitions**, resolved through the foreign-key column one of them already holds.

A developer adds a **Relation property** to a Table definition: a member typed as the *other* Table definition, carrying the cardinality in its own type, annotated with the name of the foreign-key property that resolves it.

```csharp
[Table("public.books")]
public partial class BookTable
{
   [PrimaryKey] [Generated] public long BookId { get; set; }
   public string Title { get; set; }
   public long? AuthorId { get; set; }
   public long? EditorId { get; set; }

   [Relation(nameof(AuthorId))]
   public AuthorTable? Author { get; set; }

   [Relation(nameof(EditorId))]
   public AuthorTable? Editor { get; set; }
}

[Table("public.authors")]
public partial class AuthorTable
{
   [PrimaryKey] [Generated] public long AuthorId { get; set; }
   public string Name { get; set; }

   [Relation(nameof(BookTable.AuthorId))]
   public List<BookTable> Books { get; set; }
}
```

Two capabilities follow, and the first is free:

**Filtering and ordering across a relation** needs no new API at all. Once the relation is registered, an ordinary predicate reaches through it and the provider emits the join:

```csharp
var booksByTolkien = await repository.Query()
   .Where(x => x.Author.Name == "Tolkien")
   .OrderBy(x => x.Author.Name)
   .ToListAsync(ct);
```

**Materializing the related rows** is explicit and opt-in:

```csharp
var authors = await authorRepository.Query()
   .Include(x => x.Books)
      .ThenInclude(x => x.Editor)
   .ToListAsync(ct);
```

The **Query surface** stops being single-table. It stays read-only, and it spans tables only along a declared Relation.

## User Stories

1. As a developer, I want to declare that one **Table definition** references another, so that the relationship lives in the same place as the columns instead of being re-derived in every hand-written query.
2. As a developer, I want to name the foreign-key property explicitly when I declare a **Relation**, so that a wrong guess is impossible.
3. As a developer, I want `nameof` to keep that foreign-key name compile-checked, so that renaming the property is caught at build time rather than at run time.
4. As a developer, I want the cardinality to come from the **Relation property**'s own type, so that I cannot declare a to-one relation on a collection or vice versa.
5. As a developer, I want to declare a to-one relation by typing the property as the other Table definition, so that the target needs no separate argument.
6. As a developer, I want to declare a to-many relation by typing the property as a collection of the other Table definition, so that one attribute covers both directions.
7. As a developer, I want to declare only the direction I actually need, so that a child-to-parent relation does not force me to add a collection to the parent.
8. As a developer, I want to declare two relations at the same target from one Table definition, so that `CreatedByUserId` and `UpdatedByUserId` can both resolve to the user table.
9. As a developer, I want to declare a relation that targets the declaring Table definition itself, so that hierarchies work.
10. As a developer, I want to declare both directions of a self-reference, so that I can navigate up to a parent and down to children.
11. As a developer, I want many-to-many to work by declaring two relations through a join table that is itself a Table definition, so that no separate concept exists to learn.
12. As a developer, I want a **Relation property** to be exempt from column mapping, so that adding one does not create a column that does not exist.
13. As a developer, I want a Relation property to be exempt from the query-mappable-type check, so that declaring one does not warn about an unmappable property type.
14. As a developer, I want a Relation property to be exempt from lookup generation, so that no `GetBy`/`DeleteBy` method appears for something that is not a column.
15. As a developer, I want the generated data type to carry a mirrored Relation property whose far end is the target's *generated data type*, so that the shape I query is navigable.
16. As a developer, I want a mirrored to-many property to be a non-null collection initialized to empty, so that I never null-check a collection.
17. As a developer, I want the create and update command types to be untouched by relations, so that the mutation shapes stay exactly as flat as the table they write to.
18. As a developer, I want the generated repository interface to be unchanged, so that an existing hand-written implementation of it keeps compiling.
19. As a developer, I want to filter a query by a column on the other side of a relation, so that I can find children by a parent's attribute without writing SQL.
20. As a developer, I want to order a query by a column on the other side of a relation, so that paging by a parent's attribute works.
21. As a developer, I want to filter across two hops of relations, so that a grandparent's column is reachable.
22. As a developer, I want filtering across a relation to need no new API, so that everything I already know about the query surface still applies.
23. As a developer, I want to materialize the related rows only when I ask, so that a query never pays for data I did not request.
24. As a developer, I want a Relation property to be null or empty when I did not ask for it, so that "not loaded" is never mistaken for "loaded and absent" in a way that triggers a second database round trip behind my back.
25. As a developer, I want to chain a second level of materialization onto the first, so that a parent, its children and their children arrive together.
26. As a developer, I want to chain materialization through a collection, so that the level below a to-many relation is reachable.
27. As a developer, I want to compose other operators between the two halves of a chained materialization, so that the order I write things in does not silently change what loads.
28. As a developer, I want to constrain which related rows load for a to-many relation, so that I can scope a detail query independently of the main one.
29. As a developer, I want a to-one relation to always behave as an outer join, so that a child whose foreign key points nowhere is still returned.
30. As a developer, I want a predicate on the far side of a relation to narrow the result as I would expect, so that outer-join semantics do not surprise me when I filter.
31. As a developer, I want materialization to work with every awaiting operator the query surface already offers, so that relations do not carve out an exception to the async story.
32. As a developer, I want materialization to work when I consume the query as an asynchronous stream, so that a large read can still be streamed.
33. As a developer, I want a query with materialization to run inside my ambient transaction, so that I read my own uncommitted writes even when the provider issues more than one statement.
34. As a developer, I want to compose a query with materialization before a transaction begins and still have it run inside that transaction, so that composition order does not silently bind me to the wrong connection.
35. As a developer, I want an untranslatable expression involving a relation to raise the library's own translation exception, so that the failure looks like every other query failure.
36. As a developer, I want a database failure in a query with materialization to raise the library's own query exception carrying the SQL, so that I can diagnose it.
37. As a developer, I want a query with materialization enumerated after the connection is disposed to raise a clear lifetime error, so that the cause is obvious.
38. As a developer, I want no third-party namespace import to be needed to materialize a relation, so that the dependency underneath stays invisible.
39. As a developer, I want a build error when the foreign-key property I named does not exist, so that a typo fails immediately.
40. As a developer, I want a build error when the foreign-key property's type cannot match the target's primary key, so that a wrong join is impossible.
41. As a developer, I want a build error when the relation target is not a Table definition in this compilation, so that an unresolvable relation is caught at build time rather than as a missing-table error at run time.
42. As a developer, I want a build error when a to-one Relation property is not nullable, so that the declared shape does not promise something an outer join cannot guarantee.
43. As a developer, I want a build error when a Relation property's type is neither a Table definition nor an accepted collection of one, so that an unsupported declaration is named rather than silently ignored.
44. As a developer, I want a build error when a Relation property lacks a public getter or setter, so that its shape rules match the ones columns already follow.
45. As a developer, I want a build error when I combine the relation attribute with a column attribute on the same property, so that a contradictory declaration is rejected.
46. As a developer, I want an invalid relation to be reported without abandoning the rest of the table's generation, so that one real error is not buried under cascading type-not-found noise.
47. As a developer, I want declaring a relation to have no effect on the Dapper surface, so that the generated create, read, update and delete methods keep emitting the single-table SQL they always did.
48. As a developer, I want declaring a relation to have no effect on migrations, so that the schema stays under my control and the annotation stays a claim about columns that already exist.
49. As a developer, I want the extra statements a to-many materialization costs to be documented, so that I can decide whether to pay them.
50. As a developer, I want the known interaction between a main-query filter and a to-many detail query to be documented, so that a surprising result on a self-referencing hierarchy is something I was warned about.
51. As a library maintainer, I want relations resolved from the collected set of parsed Table definitions, so that there is one cross-table validation path rather than two.
52. As a library maintainer, I want relation registration to ride along inside the existing per-compilation module initializer, so that both endpoints of every relation are always registered together.
53. As a library maintainer, I want the query-surface decorator to stay in the chain after materialization is composed, so that exception translation, SQL diagnostics and connection binding keep working.
54. As a developer, I want a correlated subquery across two generated repositories to read the tables I actually named, so that the first cross-table thing I try beyond a **Relation** does not silently return the wrong rows.
55. As a developer, I want a query mixing sources from two different connections to raise a clear translation error, so that an impossible query fails loudly instead of producing nonsense.
56. As a developer, I want the front-end configuration trap that silently empties an expanded collection documented where materialization is introduced, so that I read the warning before I ship rather than after.
57. As a library maintainer, I want the relation model to satisfy what `$expand` was independently found to require, so that the conformance suite can lift its own deferral without redesigning anything.
58. As a library maintainer, I want the latent cross-root rewrite defect closed by the work that makes it reachable, so that it is not left as a known-wrong behaviour behind a newly advertised capability.

## Implementation Decisions

### Vocabulary

**Relation** and **Relation property** are added to the glossary, and **Query surface** is revised — it remains read-only but no longer "never spans tables". The provider's own word for a Relation ("association") stays out of the public surface, consistent with ADR 0004's posture that the dependency's vocabulary does not leak. "Navigation property" is rejected because it implies lazy loading and change tracking, neither of which this library has.

### The attribute

A new sealed, single-use, property-targeting attribute joins the existing five in the attributes namespace. It takes one required argument: the name of the foreign-key property that resolves the relation. It takes no target-type argument (the property's type is the target) and no target-key argument.

The target key is never expressible because it is never in doubt: exactly one non-composite primary key per Table definition is already enforced, so the far end of every Relation is that key. This also means the provider's key-based association overloads — which do not support composite keys — are sufficient, and that no growth path exists here if composite primary keys are ever added.

Cardinality is read off the **Relation property**'s type. A single Table definition type is to-one and the named foreign key lives on the declaring type; a collection of a Table definition type is to-many and the named foreign key lives on the target type. Because the type already states the cardinality, a second attribute name would only restate it — and could contradict it.

### Relation property rules

- Typed as the other **Table definition**, never as a generated data type. A source generator analyses the pre-generation compilation and cannot see its own output, so a generated type would resolve to an error type during parsing. The generator translates the declared Table definition type to its generated data type when emitting.
- To-one properties must be nullable. Relations are always outer joins, and a foreign key pointing at a missing row genuinely yields nothing.
- To-many properties may be declared as any of the common list and sequence types; the generated mirror is always a concrete list initialized to empty.
- Public getter and setter required, matching the rule columns already follow.
- Skipped by column mapping, by the query-mappable-type check, and by lookup-method generation.

### Parser changes

Property validation currently rejects every public property that is not a mappable column, and has no opt-out mechanism. The relation attribute becomes that opt-out — but only for itself. No general-purpose "not mapped" attribute is introduced; that is a separate feature with a wider contract.

Cross-table work moves to the stage that sees all parsed Table definitions at once, because the per-table output sees only one. Relation targets must be Table definitions in the same compilation, which keeps validation to a single path over models the pipeline already produced rather than a second path over metadata symbols.

Relations are one-directional and are never paired. Each declaration is self-sufficient, the provider's association metadata is itself one-directional, and pairing would add a matching rule plus ambiguity diagnostics in exchange for nothing at translation time. A cycle is therefore the normal case, not an edge case: a child-to-parent relation plus a parent-to-children relation already is one.

### Generated code

The generated data type gains one mirrored **Relation property** per declaration. The create and update command types gain nothing. The repository interface gains nothing, so materialization must be an extension method rather than an interface member.

The mapping-registration builder gains a `Relation` method, overloaded on whether the property is a single target or a sequence of targets — one name, because a caller should not have to hunt for the right one, and the argument types make the intent clear at the call site. Because a concrete list property satisfies both overloads for the compiler, the generator emits explicit type arguments; hand-written calls resolve without them, since the sequence overload is the more specific.

Registration is emitted inside the existing per-compilation module initializer, in the same builder callback that registers the entity's columns. Association metadata resolves lazily at query-build time, so registration order is irrelevant — verified against the provider, including across separate builder instances and even when a query ran before the target was registered. Combined with the same-compilation rule, this closes the provider's silent failure mode where an unregistered target falls back to a default table name and surfaces as a missing-relation error from PostgreSQL.

Relations are always registered as nullable joins. That is also the provider's default, but it is stated explicitly so a consumer flipping the provider's global nullable-metadata setting cannot change the meaning of generated code.

### The query surface

**Filtering and ordering need no new API.** A predicate reaching through a Relation property translates once the association is registered. The provider's join optimization collapses the outer join to an inner join when a predicate lands on the joined table, which is exactly the desired result — so the "always outer join" decision costs nothing in the filter case.

**Materialization** adds `Include` and `ThenInclude` extension methods, in the same public extensions class that already carries the awaiting operators, so no third-party namespace import is needed. A zero-member marker interface generic in the entity and the previously-included property carries the type information `ThenInclude` needs to bind. A member-chain-only design was rejected because a chain cannot traverse a collection in C# — the same wall that led EF Core to `ThenInclude`. A filtered overload for to-many relations lets a detail query be scoped independently.

**Includes are recorded as the library's own expression nodes and rewritten into provider calls at execution time**, inside the same resolution step that already rewrites the query root. This is forced, not stylistic. The provider's eager-loading operator ends by hard-casting the result of `source.Provider.CreateQuery(...)` to a provider-internal query interface, and it returns its own queryable type:

```csharp
return new LoadWithQueryable<TEntity, TProperty>(
   (IExpressionQuery<TEntity>)queryable.Provider.CreateQuery<TEntity>(expression));
```

Calling it at composition time therefore fails outright against this library's decorator, and the documented workaround — delegating the decorator's `Provider` to the provider's own — would bind the query to a connection and transaction at composition time and drop the decorator from the chain. That would break the already-tested guarantee that a query composed before a transaction begins still runs inside it, and would silently disable exception translation, SQL diagnostics and the disposed-connection error for everything composed after an include.

Rewriting at execution time preserves all of those, and has a bonus: the provider's chaining marker does not survive intervening operators, so it requires its second-level call to immediately follow the first. Because the library emits the pair contiguously at execution time, an operator may sit between `Include` and `ThenInclude` in consumer code.

There is no lazy loading. Without an explicit include, a **Relation property** stays null or empty. The generated data types are plain classes with no proxying or change tracking, and nothing in the library could populate them after materialization.

### Eager-loading cost, measured

A to-one include folds into the enclosing statement as a join. A to-many include costs one additional statement **per level**, and each detail statement re-runs the parent query as a derived table rather than passing a list of keys. This is the provider's split-query strategy and it is not configurable: the option that once controlled it was obsoleted in the provider's 3.2 release and removed in 4.0, and the version in use has neither it nor a replacement. **No change to context creation is required** — this corrects a constraint recorded during triage that was true of a much older provider version.

One consequence is accepted and documented rather than worked around: a filter on the main query also constrains a to-many detail query, because the detail query re-derives its parents from the main query. The provider's maintainers confirm this as intended. On a self-referencing hierarchy the result is surprising, and the answer is the filtered include overload. Working around it would mean generating detail queries by hand, which is the opposite of ADR 0004's reason for taking a provider at all.

### A front-end trap this feature walks into

The OData conformance work established, empirically, that expansion over this provider **does** function and needs exactly what this spec provides — a relation declared on the member with an explicit foreign-key property, which independently corroborates the declaration model chosen here. It also established something sharper: with the front-end's null-propagation rewriting left at its default, **an expanded collection comes back empty, silently** — the detail queries are issued, the rows are fetched, and then discarded.

That is this feature's most dangerous interaction with the outside world, and it is invisible from inside the library: the query surface behaves correctly, the statements run, and the consumer sees empty collections with no exception. It cannot be fixed from here — the default is chosen by matching the query provider's namespace against a hardcoded list that this library's provider is not on. The library README's relations section must carry the warning at the point where materialization is introduced, not only in the OData section, because a consumer reading about `Include` is exactly the consumer about to hit it.

This also strengthens the case the conformance suite already makes: that suite's misconfiguration regression test is described in its own spec as "a weaker guard than the risk warrants" precisely because the worst symptom — silently empty expansions — was out of its scope. Once relations exist, that symptom becomes testable there.

### The cross-root rewrite, fixed here

The expression-tree rewriter that swaps the decorator for the provider's root replaces **every** decorator constant it finds with the one root it was handed, without checking which query source that decorator belongs to. Two different query sources in one expression therefore collapse onto the same root.

This was recorded as a latent issue by the OData conformance spec, which correctly judged it unreachable from anything in that scope — it needs two queryables in one expression — and noted it should be revisited with `$expand`. **This spec is where it becomes reachable, and it is in scope here.**

The reason is not hypothetical. This work tells consumers that cross-table querying works, and a correlated subquery across two generated repositories is one of the first things they will reach for:

```csharp
bookRepository.Query().Where(b => authorRepository.Query().Any(a => a.AuthorId == b.AuthorId))
```

Both `Query()` calls produce root decorators. Today both are rewritten to whichever root the executing query resolved, so the inner subquery silently reads the outer query's table. That is wrong results, not an error — the worst available failure mode, and one that only becomes reachable because relations make cross-table querying a thing consumers are told to do.

The fix is to resolve each decorator constant against **its own** query source rather than against a single root handed to the rewriter. Because every generated repository over one connection shares the same `Linq` connector and therefore the same underlying context, two sources over the same connection both resolve correctly and the correlated subquery works as written. Two sources over *different* connections cannot be one query and must raise the library's translation exception rather than produce nonsense.

This also matters directly for the filtered to-many include: its scoping lambda takes the detail sequence as a parameter, so it introduces no second constant on its own — but a consumer is free to reference another repository's query inside it, and that path lands on exactly the same defect.

### Diagnostics

Seven new diagnostics, all at error severity, covering: a foreign-key property that does not exist; a foreign-key type that cannot match the target's primary key; a target that is not a Table definition in this compilation; a to-one Relation property that is not nullable; an unsupported Relation property type; an unsupported Relation property shape; and the relation attribute combined with a column attribute on the same property.

Each drops **only the offending relation** and lets the rest of the table generate. This departs from every existing hard error, which abandons the table. The reason is diagnostic quality: abandoning the table would suppress its generated data type and cascade into a wall of type-not-found errors that buries the one message describing the actual mistake. It applies at error severity the philosophy the query-mappable-type warning already follows.

A name-collision diagnostic was considered and rejected as unreachable: relation properties and column properties are C# members of the same class, so the language already forbids the collision, and every generated member derives from a distinct declared property.

All new diagnostics must be added to the unshipped analyzer-release file or the analyzer build fails its own release-tracking rule.

### What does not change

The Dapper surface. Its generated SQL is baked at generation time with flat column aliasing and a flat parameter dictionary; there is no split-column machinery, and joined reads there would be new machinery for no stated need. Migrations and DDL — a Relation is a mapping claim about columns that already exist, and the table attribute has never emitted DDL. The consequence, which is intentional, is that nothing verifies a declared Relation against a real foreign key: a wrong declaration is a wrong join at run time, not a build error. Also unchanged: the scalar type conversions registered on the shared mapping schema, and the signature of the generated query method.

### Versioning and documentation

Version bumped to `0.32.0` (MINOR). Everything is additive: a new attribute, new properties on generated data types, new extension methods, new builder overloads. The property-validation change only skips properties carrying an attribute no existing code can have. The target is `0.32.0` whether or not the OData conformance work — which takes `0.31.0` to `0.31.1` — lands first, so the two are safe to sequence in either order.

Two adjacent pieces of work touch the same surfaces and should be sequenced with awareness of it, not merged into this one. The filed idea for carrying column nullability into generated mappings changes the same public mapping builder these relation overloads are added to, so whichever lands second inherits a slightly different builder than its own notes describe. And the conformance work lifts the SQL-rendering members onto the internal decorator interface — the same interface the cross-root rewrite fix above changes — so the two edits land in one small file and should not be attempted in parallel.

ADR 0005 records the relation model, the execution-time include translation and the cross-root fix; ADR 0004's consequence stating that cross-table querying is not unlocked points forward to it rather than being rewritten, so the record that single-table was a reasoned deferral survives. Both are already written — the glossary terms and the ADR landed with this spec, matching how ADR 0004 landed with the spec it belongs to.

The library README's claim that cross-table queries are not supported is removed, the generated-types table gains the Relation property, the build-time diagnostics table gains the new identifiers, a relations section is added covering declaration, filtering, materialization, the per-level statement cost and the detail-filter interaction, and the root README gains a bullet. README prose stays user-facing: no ADR links, no changelog, no roadmap, no test notes.

## Testing Decisions

A good test here asserts what a consumer can observe: rows returned, exception types, transaction visibility, and build diagnostics. It does not assert on the generated join syntax, the alias names the provider picks, or the internal structure of the include rewriter. Generated SQL is asserted only where behaviour is otherwise unobservable — the existing tests do this at exactly three sites, and the same restraint applies here.

All three seams already exist. No new seam is introduced.

### Primary seam: the generated-repository integration tests

Real PostgreSQL through Testcontainers, real generated repositories, the ambient transaction the shared test base opens and rolls back. This is the highest available seam: one test exercises the parser, the emitted mapping, the module initializer, the connector, the decorator, the include rewriter, the provider and the database together. Prior art is the existing query test class, whose nineteen facts established this pattern — and whose own class comment names the same chain of components as its justification.

Relation tests go in a sibling class in the same directory rather than into the existing one, which is already past three hundred lines. This follows the convention's file-size guidance and its explicit ban on splitting a type across files to dodge that guidance; a new concern gets a new class.

Four new fixture **Table definitions** are added, following the precedent that the profile fixture was introduced as a new table rather than by widening the user fixture, so existing tests stay untouched:

- An author table with a nullable self-referencing foreign key, declaring a to-one mentor and a to-many mentees, plus a to-many books.
- A book table with two nullable foreign keys at the author table, declaring two to-one relations at the same target, plus a to-many toward the join table.
- A tag table declaring a to-many toward the join table.
- A join table declaring a to-one at each side.

Between them these cover: to-one and to-many filtering, to-one and to-many materialization, multi-level materialization, many-to-many through a join table that is itself a Table definition, two relations at one target, self-reference in both directions, and outer-join semantics via the nullable foreign keys.

Their DDL is created directly in the test fixture rather than as migrations, because the migration tests assert on the exact set of migrations the test assembly ships. The existing profile fixture is created the same way and carries a comment saying so.

What must be proven at this seam:

- Filtering and ordering by a column across a to-one relation, and across two hops.
- Filtering across a to-many relation.
- A row whose foreign key is null is still returned by an unfiltered query, and is excluded once a predicate lands on the far side.
- To-one materialization populates the mirrored property.
- To-many materialization populates the mirrored collection, and leaves it empty rather than null when there are no related rows.
- A Relation property is null or empty when materialization was not requested.
- Chained materialization through a to-one and through a to-many.
- Chained materialization with an intervening operator between the two calls — the case the provider itself would reject.
- Filtered to-many materialization loads only the scoped rows.
- Materialization combined with every awaiting operator and with asynchronous streaming.
- Materialization inside a transaction sees uncommitted writes made through the Dapper surface, including for a to-many relation where more than one statement runs.
- A query with materialization composed before a transaction begins runs inside that transaction.
- The library's translation exception, the library's query exception carrying SQL, and the disposed-connection error all still surface from a query with materialization — these are the guarantees the execution-time rewrite exists to protect, so they are the tests that would catch a regression to composition-time forwarding.
- Many-to-many traversal across the join table in both directions.
- Self-referencing materialization in both directions.
- The main-query-filter interaction with a to-many detail query, asserting what the database actually returns rather than what the design assumed, with the filtered overload demonstrated as the answer.

### Secondary seam: the analyzer tests

Inline source compiled against the hand-maintained runtime stubs, with no reference to the real library. The new attribute and the new builder overloads must be added to those stubs, or every relation test fails to compile for the wrong reason.

Prior art is the existing generator test class. What is asserted here and nowhere else:

- Each of the seven new diagnostics, from a minimal source that triggers only it.
- That an invalid relation still produces generated sources — the same assertion the existing unmappable-type test makes, which is the behaviour that keeps one error from cascading.
- The shape of the emitted mirrored properties and the emitted relation registration, as a smoke check only.
- That a valid to-one and to-many pair produces no diagnostics.

### Unit seam: the Linq connector tests

The include rewriter and the cross-root fix, both of which have a genuine expression-in, expression-out contract. Prior art is the existing root-rewriter test class, which tests the analogous rewrite against a hand-written fake and a plain constant expression, with no provider involvement at all.

For the include rewriter: a single include, a chained include, an include with an intervening operator, a filtered include, and a tree containing no includes passing through unchanged.

For the cross-root fix: an expression containing two distinct sources rewrites each to its own root rather than collapsing both onto one — this is the test that would have caught the existing defect, and it needs only two hand-written fakes. Plus the error case for sources that cannot belong to one query. The corresponding end-to-end proof belongs at the primary seam, as a correlated subquery across two generated repositories returning the rows the query actually names.

### Compile-time canary

The hand-written fake implementation of a generated repository interface, which exists in the integration project, breaks if the interface changes. It should keep compiling untouched — that is the assertion that materialization stayed an extension method.

## Out of Scope

- **Mutation across relations.** No cascading writes, no creating a parent and a child in one call, no writing through a **Relation property**. The Query surface stays read-only and the command types stay flat.
- **Cross-assembly relations.** A relation target must be a **Table definition** in the same compilation. Resolving a target from a referenced assembly is possible via metadata symbols but adds a second validation path for no established need.
- **Composite foreign keys and composite primary keys.** A single non-composite primary key is already enforced, and the provider's key-based association API does not support composite keys either.
- **A general-purpose "not mapped" attribute.** The relation attribute opts its own property out of column mapping. A broad opt-out is a wider contract change and a separate feature.
- **Generating or validating database foreign keys.** A Relation is a mapping claim about columns that already exist. Nothing checks it against the real schema.
- **Relations on the Dapper surface.** The generated create, read, update and delete methods stay single-table.
- **Configuring the eager-loading strategy.** The provider offers no such control in the version in use, and reimplementing detail queries to get one is out of the question.
- **OData `$expand` conformance.** This work adds no front-end dependency of its own. Proving that `$expand` actually drives the relation model correctly belongs to the OData conformance suite, which currently lists `$expand` as out of scope pending exactly this spec, and whose own research is carried into the decisions above. ADR 0004 was amended to record that front-end conformance is proven out-of-band in a non-packable project, so that suite — not this one — is where the two meet.
- **Guardrails on materialization depth or breadth.** No include-depth cap and no limit on how many relations one query may materialize. Constraining an exposed query surface remains the consumer's responsibility, consistent with every other adapter in this library.
- **Exposing the provider's context, table type, association types or eager-loading types** through any signature.

## Further Notes

### Risks carried knowingly

1. **The execution-time include rewrite is new machinery on the hot path.** It sits beside the existing root rewrite, which is well-tested and structurally similar, but it is more than a constant swap: it must strip its own nodes, emit a contiguous provider call chain against the resolved root, and re-root the remainder. The mitigation is that the integration tests protecting exception translation, SQL diagnostics and transaction binding all run *through* a materialized query, so a regression to composition-time forwarding fails loudly rather than silently.
2. **Overload resolution on the registration builder is ambiguous for a concrete list property.** The generator sidesteps it with explicit type arguments. A hand-written call resolves correctly because the sequence overload is more specific, but this is worth a test at the analyzer seam so a future change to the overload set does not silently break generated code.
3. **The per-level statement cost of to-many materialization is easy to underestimate**, because each detail statement re-runs the parent query as a derived table rather than passing keys. A three-level include over collections is four statements, each re-deriving its ancestors. This is documented rather than defended.
4. **The main-query-filter interaction is a genuine footgun** on self-referencing hierarchies, accepted because the alternative is generating detail queries by hand. The test asserting it exists partly to detect the day the provider changes the behaviour.
5. **Nothing verifies a Relation against the real schema.** A relation naming the wrong foreign-key column type-checks, registers, and produces a wrong join at run time. This mirrors how column names already work in this library.
6. **The cross-root fix touches code every query already runs through.** It is a small change to a small file, but it is on the path of all nineteen existing query tests, which is both the risk and the mitigation — a mistake there fails loudly and immediately rather than only in the new cross-source case.
7. **The most damaging failure mode of this feature is not in this library.** A front-end left at its default configuration silently empties expanded collections. Every mitigation available is documentation, and documentation reaches the consumer who reads it first.

### Sequencing suggestion

The attribute and the parser changes first, with the diagnostics, since everything downstream depends on the parsed relation model. Then the generated mirrored properties and the registration builder overloads, which makes filtering across a relation work end to end with no query-surface changes at all — a natural checkpoint with real integration coverage. Then the include rewrite, the extension methods and the marker interface. The cross-root fix is independent of all of it and can go first or last; first is better, because it is the one change that could break existing behaviour, and doing it against nineteen passing query tests and no new features is the cleanest signal available. Documentation and the version bump last.

### Triage disposition

The table-relations idea file is fully superseded by this spec: all seven of its open questions are answered here, both of its recorded constraints are carried forward — one of them corrected, since the multiple-query mode it warned about no longer exists in the provider — and its observation that consuming applications prefer flat joins is why filtering across a relation needs no new API. It should be deleted when this spec lands, rather than left as a second source of truth beside it.
