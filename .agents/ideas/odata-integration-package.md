# An opt-in OData integration package

Status: needs-triage

## Motivation

A consumer wiring an OData **Query front-end** onto the **Query surface** must disable the front-end's null-propagation rewriting, and must narrow its allowed-function set to exclude the functions that do not translate. Neither is discoverable. The first is a single setting whose default is chosen by matching the query provider's namespace against a hardcoded list of Microsoft providers — a list this library's provider is not on and cannot join from our side. Left at its default it does not fail loudly; it degrades predicates into a form PostgreSQL cannot index, breaks one string function outright, and returns wrong rows for collection quantifiers.

The upstream request to make the correct behaviour automatic has been open since 2022, and its author already noted that the workaround "is not easy to find". Four years on, every consumer still has to find it independently.

The conformance suite makes the requirement testable and its README makes it documented, but documentation only helps a consumer who reads it before shipping rather than after.

## Goal

Make the correct configuration the default for consumers who want an OData front-end, without putting a front-end dependency into the packages every consumer already gets.

## Decisions (locked)

None. Two constraints, though:

- [ADR 0004](../../docs/adr/0004-linq2db-as-the-queryable-provider.md) commits the shipped packages to being front-end agnostic. Anything here must be a separate opt-in package, not an addition to the core one.
- The same ADR records why opt-in packaging was unavailable for the query surface itself: a satellite package cannot add a member to the main entry point. That constraint does not apply here — a preconfigured front-end component is not a member of anything we own — but it is the reason to check the assumption rather than inherit it.

## Out of scope

- Anything that adds a front-end dependency to the core library or the tool.
- Supporting front-ends other than OData. If a second one ever needs this treatment, that is its own decision.

## Open questions

- Is a third shipped package worth it, against the alternative of documentation alone? The maintenance cost is real: the front-end's major versions break, and its release cadence is not ours.
- Which framework versions would it target, given the front-end's stable line ships a single older assembly and its next major line drops it?
- Does it ship only configuration, or also the eager-materialization behaviour that turns a translation failure into a proper error status instead of a truncated response?
- How does it version — with the library, or independently on the front-end's cadence?
- Would contributing the provider's namespace upstream make this package unnecessary, and is that worth attempting first? See the sibling idea.
- If the upstream fix lands later, does this package become a liability that has to be deprecated?
