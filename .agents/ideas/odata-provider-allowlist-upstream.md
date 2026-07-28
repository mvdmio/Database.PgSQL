# Get the query provider recognised upstream

Status: needs-triage

## Motivation

ASP.NET Core OData decides how defensively to rewrite a filter by matching the query provider's namespace against a hardcoded list of Microsoft providers. Providers on the list are trusted to handle nulls in SQL and get clean expression trees. Everything else — including the provider behind the **Query surface** — is treated as an in-memory sequence and gets every property access wrapped in a null guard.

For this library the consequences are concrete: one string function stops translating entirely, collection quantifiers return wrong rows, and every predicate arrives at PostgreSQL in a form no index can serve. All of it disappears with one setting the consumer has to know to set.

Fixing it at the source would make the correct behaviour the default for every consumer of this library, and for everyone else using the same provider. The request already exists upstream and has been open since 2022 without action; the person who filed it asked what conditions a provider would need to meet to be added, and never got an answer.

## Goal

Have the provider recognised by the front-end so that its default rewriting is appropriate, removing the need for consumers to configure anything — or, failing that, establish that the mechanism will not be opened up, so the alternatives can be judged against a definite answer rather than an open ticket.

## Decisions (locked)

None.

- One thing worth stating plainly: this is not ours to merge. Every option here depends on maintainers of two other projects agreeing, on their own timelines. Median resolution time for non-trivial issues in this area, measured across the relevant history, runs to months and sometimes years.

## Out of scope

- Working around the problem locally. That is covered by the conformance suite's documentation and by the sibling opt-in package idea.
- Changing the null-comparison mode to compensate. It is the specification-correct choice and [ADR 0004](../../docs/adr/0004-linq2db-as-the-queryable-provider.md) locked it in.

## Open questions

- Is adding a namespace to the list the right ask, or is the better contribution a way for a provider to declare its own capability — which is the more general fix and therefore the harder sell?
- Which project should the change land in: the front-end, which owns the list, or the provider, which could expose something the front-end can detect?
- What would the front-end's maintainers need as evidence that the provider handles nulls in SQL correctly? A conformance suite is exactly that evidence, which makes attempting this cheaper after the suite exists than before.
- Is it worth the effort given the workaround is one line, or is the value mostly in the discoverability rather than the correctness?
- Does the front-end's next major line change this mechanism, making a contribution against the current line wasted?
