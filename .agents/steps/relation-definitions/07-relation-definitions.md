# 07 — Record the decision and ship the break

Status: pending

## What to build

The feature is finished; this step makes it findable and releasable. Steps 01 to 06 deliberately left every
user-facing document untouched so no reader ever met a half-migrated description. All of it lands here, describing the
shape the branch actually ends in.

**An ADR.** Records that a **Relation** is declared by a **Relation definition** carrying **Relation keys** and an
optional **Relation condition**, and why one mechanism was chosen over two. It supersedes the declaration half of ADR
0005 and absorbs ADR 0006's composite-key story, which pairs make unremarkable. ADR 0005 stays in place pointing
forward — the same pattern it used on ADR 0004 — and ADR 0006 likewise. Cover what the alternatives were and what the
choice costs: every relation now takes about five lines where a plain one took two, and that price is on the record
deliberately.

**The library README.** Rewrite the relations section with the old declaration shown beside the new one, so a
developer upgrading can convert a Table definition mechanically without guessing. Explain the pairs, the condition,
the cardinality, the uniqueness claim and what still stays the developer's own: the library creates no schema, so a
developer who wants the database to refuse a link pointing at a row that does not exist still writes the generated
column and its foreign key in a migration by hand. Update the attributes table's `[Relation]` row, the composite-key
section wherever it names a retired rule, the tenancy section's account of the cross-tenant warning, and the
requirements list. The build-time diagnostics table gains `PGSQL0028` through `PGSQL0035` and loses `PGSQL0012`,
`PGSQL0013` and `PGSQL0019`.

**The root README.** Update the relations bullet if this feature made anything it says untrue.

Both READMEs are user-facing only: no ADR links, no changelog, no roadmap, no test notes.

**`CONTEXT.md`** is already updated for this feature — **Relation** rewritten, **Relation definition**, **Relation
key** and **Relation condition** added, **Relation property** and **Key order** corrected. Verify it matches what
shipped and correct only what drifted; do not rewrite it.

**Release tracking.** Roll the rules the earlier steps added into the shipped release file the same way the 0.36
release was handled, with the three retired rules recorded as removed, and leave the unshipped file in the state that
pattern leaves it.

**The version.** This breaks a published package, which pre-1.0 is a MINOR bump: `0.36.0` to `0.37.0`.

## Acceptance criteria

- [ ] A new ADR records the class-based declaration, supersedes the declaration half of ADR 0005, absorbs ADR 0006's
      composite-key story, and states the per-relation line cost as an accepted trade.
- [ ] ADR 0005 and ADR 0006 stay in place and point forward to it.
- [ ] The library README's relations section shows the old declaration beside the new one, and covers pairs,
      conditions, cardinality, the uniqueness claim, and what the developer still owns in their own migrations.
- [ ] The library README's diagnostics table lists `PGSQL0028` through `PGSQL0035` and no longer lists `PGSQL0012`,
      `PGSQL0013` or `PGSQL0019`; the attributes table, composite-key section, tenancy section and requirements list
      say nothing the code no longer does.
- [ ] Neither README carries an ADR link, changelog, roadmap or test note.
- [ ] `CONTEXT.md` matches what shipped.
- [ ] Analyzer release tracking is rolled to a `0.37` release, with the three retired rules recorded as removed.
- [ ] `<PgSqlVersion>` is `0.37.0`.
- [ ] `dotnet format --verify-no-changes`, `dotnet build` and `dotnet test` are all green (Docker running).
