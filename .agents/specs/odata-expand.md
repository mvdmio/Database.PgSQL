# OData `$expand` over the query surface

Status: ready-for-agent

## Problem Statement

A developer exposing a generated repository through an OData **Query front-end** cannot find out whether `$expand`
works. The conformance suite's results table says `untested — needs a relation model, which the library does not have
yet`, and its "deliberately not covered" section says the same. Both statements are now false: the library gained a
**Relation** model, so the reason for the gap is gone but the gap is not.

Worse than the gap is what fills it. The packed library README carries a boxed warning telling readers that with the
ASP.NET Core OData defaults left alone, an expanded collection comes back **empty and without any error** — "the detail
queries run, the rows are fetched, and the result is then discarded". ADR 0005 lists it as the feature's "most damaging
failure mode". The suite's own misconfiguration regression tests name the symptom in their class documentation and then
declare it out of scope. Nothing anywhere tests it. It is the strongest promise the package makes about a front-end it
does not depend on, and it rests entirely on reasoning.

Two smaller claims are in the same position. The suite's misconfiguration table states that a collection `all()`
returns the wrong rows, marked `(not tested here — it needs a relation model)`. And filtering or ordering through a
**Relation property** from a query string — `$filter=Author/Name eq 'tolkien'` — has never been exercised through a
front-end at all, though the **Query surface** has supported it since the Relation model landed.

So a developer reading the documentation today gets three assertions about silently wrong results, and no evidence for
any of them.

## Solution

Extend the OData conformance suite to cover every query-string construct the Relation model unblocked, and replace each
untested assertion with a test that pins observed behaviour.

The suite's method does not change: a query string is parsed into OData query options in-process, applied to a generated
repository's `Query()`, and the resulting rows and SQL are asserted against a real PostgreSQL database. What changes is
that the fixture gains a pair of **Table definitions** that declare **Relations** to each other, so a query string has
something to expand, reach through, and quantify over.

The deliverable is evidence-shaped rather than outcome-shaped. `$expand` may translate cleanly, translate into more
statements than a reader expects, or refuse outright — the suite records whichever it is, in the same results tables
that already record `matchespattern` as a refusal. A refusal is a conformance result, not a defect to fix in this pass.

Afterwards a developer reading the OData walkthrough finds `$expand` in the results table with the same detail as every
other option, finds the nested options spelled out, finds the misconfiguration symptoms demonstrated by name, and finds
an expansion-depth cap in the recommended configuration with the reason it matters written next to it.

## User Stories

1. As a developer exposing a generated repository over OData, I want the conformance results table to state whether
   `$expand` works, so that I can decide whether to offer it to my clients before I ship rather than after.
2. As a developer exposing a generated repository over OData, I want to know what `$expand` reaches the database as, so
   that I can judge the cost of an endpoint that offers it.
3. As a developer exposing a generated repository over OData, I want to know whether expanding to many rows costs
   statements beyond the main query while expanding to one row does not, so that I can cap the shapes my clients may ask
   for.
4. As a developer exposing a generated repository over OData, I want to know whether an expansion whose foreign key is
   null comes back as an absent **Relation property** or as an error, so that my endpoint does not fail on ordinary data.
5. As a developer exposing a generated repository over OData, I want to know whether expanding a **Relation** with no
   matching rows yields an empty collection, so that my clients can distinguish "none" from "not asked for".
6. As a developer exposing a generated repository over OData, I want each nested expand option I might allow — filter,
   select, orderby, top, count, a further expand, levels — listed with its own result, so that I can enable them
   individually instead of guessing at the whole group.
7. As a developer exposing a generated repository over OData, I want to know what `$expand=*` does against a model built
   from **Table definitions**, so that I can decide whether to allow it at all.
8. As a developer exposing a generated repository over OData, I want the recommended configuration to cap expansion
   depth, so that a client cannot walk a cycle in my model until the database gives up.
9. As a developer exposing a generated repository over OData, I want the reason that cap exists written next to it, so
   that I do not remove it later as unexplained tuning.
10. As a developer exposing a generated repository over OData, I want a request that exceeds the depth cap to come back
    as a validation error, so that my clients get a client error rather than a server error.
11. As a developer exposing a generated repository over OData, I want to filter through a **Relation property** from a
    query string, so that my clients can select rows by a value on the related table without a bespoke endpoint.
12. As a developer exposing a generated repository over OData, I want to order by a value reached through a **Relation
    property**, so that my clients can sort by related data.
13. As a developer exposing a generated repository over OData, I want to know whether a query-string collection
    quantifier translates to SQL, so that I can offer `any` and `all` predicates with confidence.
14. As a developer exposing a generated repository over OData, I want the documented claim that `all()` returns wrong
    rows under the default settings to be demonstrated, so that I can recognise the symptom if I meet it.
15. As a developer exposing a generated repository over OData, I want the documented claim that an expanded collection
    silently comes back empty under the default settings to be demonstrated, so that I know the mandated setting is a
    mandate and not a preference.
16. As a developer who has already shipped an endpoint, I want to know whether my expansions are affected by the
    null-propagation setting, so that I can check one thing rather than audit everything.
17. As a developer exposing a generated repository over OData, I want the difference between a null-propagation symptom
    and a genuine translation refusal made clear, so that I debug the right one.
18. As a developer building an EDM model from generated data types, I want to know that a **Relation property** appears
    in the model as something a client can expand, so that I do not have to discover it by trial.
19. As a developer building an EDM model from generated data types, I want to know that the key of an expanded type has
    to be declared explicitly, so that model building does not fail on a convention that cannot find it.
20. As a developer building an EDM model from generated data types, I want to know whether an expanded type needs its
    own entity set, so that my model matches what my routes expose.
21. As a developer whose **Table definitions** declare **Relations** in both directions, I want to know that the
    resulting cycle in my EDM model is expected rather than a mistake, so that I do not go looking for a way to remove
    it.
22. As a developer reading the packed library README, I want its warning about empty expanded collections to match what
    the suite observed, so that I can trust the rest of the document.
23. As a developer reading the packed library README, I want any claim it makes that nothing verifies to be softened to
    what is verified, so that I am not steered by confident prose that turns out to be wrong.
24. As a library maintainer, I want the three untested assertions about silently wrong results replaced by tests, so that
    a change in the front-end or the query provider that invalidates them fails the build instead of misleading a reader.
25. As a library maintainer, I want the suite to keep asserting through one seam, so that a front-end upgrade breaks in
    one place rather than across a suite.
26. As a library maintainer, I want the existing conformance fixture left alone, so that every result already pinned
    stays pinned and a regression in the new work cannot be mistaken for a change in the old.
27. As a library maintainer, I want the new fixture to follow the shape the main integration suite already uses for
    **Relations**, so that the two suites can be read against each other.
28. As a library maintainer, I want a construct that refuses to translate recorded as a conformance result, so that the
    **Translation boundary** is documented per front-end exactly as it is for `$filter` functions today.
29. As a library maintainer, I want anything that looks like a defect in the library raised as its own idea rather than
    fixed inside this work, so that a characterization pass cannot turn into an open-ended one.
30. As a library maintainer, I want no new public API and no new diagnostics facility, so that this work cannot move the
    package's contract.
31. As a library maintainer, I want the version bumped only if the packed README content changes, so that the number
    tracks what consumers actually receive.
32. As a library maintainer, I want to be asked before an accepted ADR is edited, so that a correction to a recorded
    decision stays my call.
33. As an implementing agent, I want the acceptance criteria expressed as coverage and evidence rather than expected
    values, so that I do not read an unexpected observation as a failure I have to engineer away.
34. As an implementing agent, I want the list of constructs to cover stated exactly, so that "done" is countable.
35. As an implementing agent, I want to know which constructs are deliberately excluded, so that I do not add coverage
    the spec did not ask for.
36. As an implementing agent, I want to know that statement counts are not observable, so that I do not attempt an
    assertion the suite cannot make.

## Implementation Decisions

### The front-end drives; nothing is intercepted

OData does not implement `$expand` by calling an eager-loading operator. Its query options bind `$expand` as a
**projection** into its own wrapper types, selecting the **Relation property** inside that projection. Consequently the
library's `Include` and `ThenInclude` operators, and all the machinery behind them, are **not on the `$expand` path at
all**.

What commit 5f13060 actually unblocked is narrower, and this spec covers exactly that:

- the provider-level association registration emitted for each **Relation**, without which the projected member has no
  join to translate; and
- the **Relation properties** mirrored onto the generated data type, without which an EDM model has nothing to expand.

The alternative — reading the parsed expand clause and rewriting it into the library's own materialization operators —
is rejected. ADR 0004 commits the shipped packages to being front-end agnostic, and an interceptor would additionally
have to reproduce the front-end's wrapper types to preserve its serialization contract. A preconfigured front-end
component remains a separate opt-in package, tracked as its own idea.

### Scope is the whole Relation-blocked backlog, not only `$expand`

Three things were blocked on the Relation model and all three are in scope, because the fixture is the expensive part
and it is paid once:

- `$expand`, in both cardinalities, with nested options.
- Filtering and ordering through a **Relation property** from a query string.
- Collection quantifiers from a query string, including the misconfiguration symptom the suite currently documents
  without testing.

### The fixture gains a Relation-bearing pair, in its own EDM model

Two new **Table definitions** are added to the OData suite's fixture, following the shape and naming the main
integration suite already uses:

- An author **Table definition** with a key, a name, a nullable self-referencing foreign key, a to-one **Relation
  property** to itself, a to-many **Relation property** to itself, and a to-many **Relation property** to books.
- A book **Table definition** with a key, a title, a nullable foreign key to the author, and a to-one **Relation
  property** to the author.

Both carry only property types with a direct EDM equivalent, so model building cannot fail for reasons unrelated to
**Relations**.

This pair earns every construct in scope: expansion to one row and to many, nested expansion two levels deep, `$levels`
over the self-reference, a cycle in the EDM model, navigation-path filtering and ordering, and both collection
quantifiers. A join table is deliberately **not** added — it would exercise a many-to-many shape but no query-string
construct that two-level nested expansion does not already reach.

The existing conformance **Table definition** is not touched. Adding a **Relation property** to it would place a
navigable member in the existing EDM model, where convention-based model building would discover it, pull the target
type in, and change what the already-pinned `$select`, `$apply` and model-building results see. The new pair therefore
gets its own EDM model, mirroring the existing split between the clean conformance entity and the awkward-types entity.

The tables are created by committed DDL in the suite's fixture rather than by a migration, matching both existing
tables and the main integration suite's relation tables. Real foreign-key constraints are declared, even though a
**Relation** never creates or verifies one, so the fixture does not model something the database would reject.

Seed data is chosen so every assertion discriminates, in the manner the existing conformance seed already does. It must
include a mentor chain deep enough to distinguish `$levels=2` from an unbounded walk, an author with no books so an
empty expanded collection is observable, and a book with a null foreign key so expansion across a **Relation** that
finds nothing is observable.

### The second EDM model is threaded through the existing seam

The in-process driver currently hardcodes the single EDM model. It gains an optional model parameter defaulting to
today's, and the configuration type gains a second model property alongside the existing one. Every current call site
is unchanged, so nothing already pinned can shift.

In the new model, both entity types declare their key explicitly — convention-based key discovery looks for a name
derived from the generated data type's name, which is not the key a **Table definition** declares — and both are
exposed as entity sets, since a consumer exposing an expandable type would route to it.

### Expansion is enabled, and depth is capped explicitly

The recommended configuration enables the expand option, which is off by default like every other.

The validation settings state a maximum expansion depth explicitly rather than relying on the default. This follows the
precedent set by the null-propagation setting: the value is defensible either way, but the reason it matters *here* is
invisible. ADR 0005 records that **Relations** are one-directional and never paired, and that consequently "a cycle is
therefore the normal case, not an edge case" — so every consumer's EDM model contains cycles by construction, and
expansion depth is the only thing bounding a walk around one. The comment carries that reasoning.

The cap is set to admit the deepest construct in scope and reject anything beyond it, and a request that exceeds it is
pinned as a *validation* error rather than a translation failure — the same error-contract distinction the suite already
draws for blocked `$filter` functions.

### Statement counts are out of reach, and that is accepted

The suite can observe the SQL a composed query renders to, and the last statement sent through the connection. It cannot
observe how many statements a query issued, because nothing collects them.

No diagnostics facility is added to make that observable. The consequence is accepted and must be recorded rather than
worked around: for expansion to many rows, the suite asserts the rows returned and — using the existing last-statement
facility — that a detail statement reached the database at all, but it does **not** assert how many statements ran. The
packed README's claim about one additional statement per level therefore stays unverified by this work, and any wording
that implies the suite verified it is trimmed.

Expansion to one row is expected to fold into the main query, so its SQL is assertable through the existing rendering
facility in the ordinary way.

### Findings are recorded, not fixed

A construct that refuses to translate, or translates into something surprising, is a conformance result: it goes into
the results tables next to `matchespattern` and `isof`, pinned by a test. Anything that looks like a defect in the
library is raised as its own idea in the issue tracker and left there. This pass changes no library behaviour.

### Documentation obligations

- The OData suite's README loses both `$expand` deferrals, gains a filled results-table row, gains a section on
  expansion in the manner of the existing `$select` and null-comparison sections, and loses the `(not tested here —
  it needs a relation model)` qualifier from its misconfiguration table.
- The recommended-configuration type's comment saying `$expand` is out of scope is replaced.
- The packed library README's boxed warning is reconciled with what the tests observed. Three branches are
  pre-authorized: if the warning holds, trim only the statement-count implication; if the symptom holds but the stated
  mechanism does not, keep the mandate and rewrite the mechanism; if the premise is false, remove the box.
- The root README needs no change — it links to the walkthrough and never names the option.
- The spec cross-references the two existing OData ideas. Findings here feed the integration package idea's open
  question about whether such a package would ship materialization behaviour as well as configuration.

### Version and escalation

The packed README ships inside the NuGet package, so its content is published output. A change to it takes a **patch**
bump; a change confined to the test project takes none. No public API changes, so nothing larger is warranted.

One stop-and-escalate condition: if the packed README's premise turns out to be false, then ADR 0005's consequence list
contains a wrong claim about the feature's most damaging failure mode. An accepted ADR is not edited without asking.

### Domain documentation

Neither `CONTEXT.md` nor a new ADR is warranted, and the reasoning is recorded so it is not revisited by accident.

The glossary already covers this work. A `$expand` request is a **Query front-end** composing operators over the
**Query surface**; the **Query surface** entry says it spans a **Relation** by "filtering and ordering across one, and
materializing the related rows when explicitly asked", and a client sending `$expand` is asking explicitly. The entry
never claims the library's own materialization operators are the only way to ask, so nothing needs sharpening.

No ADR, because the only candidate decision is not new. That the front-end drives and the library does not intercept
follows from ADR 0004's front-end-agnostic commitment and ADR 0005's finding that this failure mode is "outside this
library and cannot be fixed from inside it". Restating it would add a document without adding a decision. Everything
else in this spec is tests and prose, which is reversible by construction.

## Testing Decisions

### What makes a good test here

The suite tests a query string against a real database and asserts what a consumer can see: the rows returned, the
total count the front-end resolved, and the SQL the query surface produced. It does not reach into the front-end's
binder, the provider's expression tree, or the library's internals. A test names the construct in the query string and
asserts the observable consequence — this is characterization, so the assertion records what was observed rather than
what was hoped for.

Assertions on SQL target the shape that matters — a join is present, a column list is narrowed, a value is
parameterized rather than inlined — rather than whole SQL strings, so that a provider upgrade that changes aliasing does
not fail a test about semantics. Where an assertion is not self-evident, the reason goes in a comment beside it, as the
existing conformance tests do.

### One seam, already present

Every test goes through the existing seam: a query string is applied to a generated repository's `Query()` through the
in-process driver, returning a value that exposes the composed queryable, the resolved total count, the rendered SQL and
the last statement sent. No new seam is introduced. Extending the driver to accept a second EDM model is a parameter on
the existing seam, not another one.

Tests go through a generated repository's `Query()` rather than the `Linq` adapter directly, because that is the seam a
consumer calls — the existing conformance base class already states this and the new one inherits the reasoning.

### Modules under test

- The **Query surface** reached through a generated repository, as composed by an OData **Query front-end**: what
  translates, what refuses, and what rows come back.
- The provider-level association registration emitted for each **Relation**, exercised indirectly — it is what makes an
  expanded or navigated member translatable at all.
- The recommended configuration itself, in that the depth cap and the enabled options are asserted by the tests that
  depend on them.

Explicitly *not* under test: the library's own materialization operators, which the `$expand` path never reaches. They
are covered by the main integration suite's relation tests.

### Test organization

A new conformance base class seeds the author-and-book graph and applies query strings against the new EDM model,
parallel to the existing one. Three new test classes hang off it: one for expansion, one for navigation-path filtering
and ordering plus quantifiers, and one for the misconfiguration symptoms. The last is named to parallel the existing
misconfiguration regression class, so the pair is obvious from a file listing; it cannot simply join that class, which
is bound to the existing conformance fixture.

Existing test classes are not modified. One existing fixture helper is: the projected-row unwrapper handles a single
level of the front-end's wrapper type, and an expanded result nests them, so it must recurse. Existing tests produce no
nested values, so their behaviour is unchanged — but three test classes depend on that helper and the change is called
out rather than buried.

### Prior art

- The existing query-option conformance tests, for naming and for the paired "assert the SQL shape, then assert the
  rows" style.
- The existing misconfiguration regression tests, for the shape of a test that runs the same query string twice — once
  with the mandated settings and once with the settings deliberately left at their defaults — and asserts the
  difference. Those tests deliberately leave the default *unpinned*, so that the assertion continues to prove what the
  front-end's provider-matching actually decides; the new ones follow that, for the same reason.
- The main integration suite's relation tests, for the fixture's table shape, its **Relation property** declarations
  and its committed DDL.
- The existing untranslatable-query and blocked-function tests, for how a refusal is pinned as a result.

### Coverage list

Countable, so that "done" is countable.

Expansion:

- to one row; to one row where the foreign key is null
- to many rows; to many rows where there are none
- nested filter, nested select, nested orderby with nested top, nested count
- a nested expansion, two levels deep
- `$levels` over the self-reference
- expand-everything
- a request exceeding the depth cap, pinned as a validation error

Through a **Relation property**:

- filtering on a value on the related table
- ordering by a value on the related table
- an `any` quantifier; an `all` quantifier

Misconfiguration, each run both with the mandated settings and with the defaults left alone:

- an expanded collection
- an `all` quantifier

## Out of Scope

- Changing any library behaviour. A defect found here is raised as an idea, not fixed.
- Adding public API, and adding any diagnostics facility — including one that would make statement counts observable.
- Intercepting the parsed expand clause and rewriting it into the library's own materialization operators.
- A preconfigured OData integration package. That remains its own idea, and stays governed by ADR 0004's
  front-end-agnostic commitment.
- Nested `$compute` and `$search`, a raw count on a navigation path, and selecting a **Relation property** without
  expanding it. These join the suite's existing "deliberately not covered" list.
- `$search`, `$compute`, `$skiptoken` and `$batch`, which were already uncovered and stay so.
- A many-to-many fixture shape.
- Hosting. No controllers, no minimal-API endpoints, no test host — unchanged from the suite's existing position, and
  the hosted failure mode stays documented rather than tested.
- Frameworks other than the one the suite targets.
- Editing an accepted ADR without asking first.
- Changing the existing conformance **Table definition** or the results already pinned against it.

## Further Notes

The documentation this work corrects is unusually load-bearing, which is why the spec spends more words on prose
obligations than a test-coverage task normally would. Three separate places — a packed README, an accepted ADR, and the
documentation on the suite's own misconfiguration tests — currently assert that an expanded collection silently comes
back empty, and they assert it with more confidence than any of them has earned. Whichever way the tests fall, the
outcome is worth having:
either the warning is substantiated and can be stated as fact, or it is overstated and has been steering consumers away
from a working feature.

The reframing in the first implementation decision is the thing most likely to be lost if this spec is skimmed. The
natural assumption — that `$expand` sits on top of the `Include` operators the Relation model shipped — is wrong, and an
implementer who holds it will look for the wrong tests, assert on the wrong statements, and read the split-query
behaviour documented for `Include` as though it applied here. What `$expand` uses is the association registration and
the mirrored **Relation properties**. Nothing else.
