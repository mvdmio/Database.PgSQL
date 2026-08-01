# 07 — Record the decision and ship the break

Status: done

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

## Outcome

A new ADR, `docs/adr/0010-relation-definitions.md`, records the class-based declaration: a Relation is stated by a
`RelationDefinition<TDeclaring, TTarget>` class naming both tables, its `Keys` and optional `Condition`. It supersedes
the declaration half of ADR 0005 and absorbs ADR 0006's composite-key story (a composite relation states one `Key(…)`
pair per column, no differently from a single-column one), while leaving both ADRs' other decisions — one-directional
relations, execution-time eager loading, the predicate-based association registration, key-order-as-cosmetic, the
nullable-key-member refusal — untouched and still describing what ships. The ADR's own headline example uses the
`private` property / `private` nested class shape from the real `PolymorphicLinkTable` fixture (not the spec's own
broken `public`-over-`private` example, which does not compile per `CS0053`), and its Consequences section states
plainly that the condition's constant does not reliably reach PostgreSQL as a literal in the association join — the
mechanism is emitted but linq2db's association path does not route through the visitor that honours `Sql.Constant`
there, per step 03's finding. `PGSQL0035`'s settled decision is folded in as its own consequence bullet. The API-surface
bullet lists every retired, new, and reused diagnostic id.

ADR 0005 and ADR 0006 each gained a pointer-forward blockquote immediately under their title, the same pattern ADR
0005 already used on ADR 0004: ADR 0005's says the declaration half is superseded by ADR 0010 and everything else
(pairing, eager loading, blast radius) still stands; ADR 0006's says the composite-relation declaration syntax is
absorbed and everything else (why composite keys were admitted, the fixed lookup name, the nullable-key-member cost
analysis) still stands.

The library README's `### Relations` section (`src/mvdmio.Database.PgSQL/README.md`) is rewritten in full: the
opening example is the new class form; an "Upgrading from the attribute form" subsection shows the old
`[Relation(nameof(...))]` declaration directly beside its new `RelationDefinition<,>` equivalent for the same
`Book`/`Author` pair, so a conversion is mechanical; a "Stating the pairs" subsection covers `Key(…)`'s two overloads,
pair-order independence, and the composite-key shape (one pair per column, in any order); a cardinality/uniqueness
subsection covers the to-one/to-many split and `PGSQL0031`'s warning-not-error uniqueness claim; a condition
subsection uses the actual `PolymorphicLinkTable`/`LinkPersonTable`/`LinkAssetTable` shape, covers omitting the
condition, reaching through another relation property, the `PGSQL0032` policing boundary, and `PGSQL0034`'s
forgotten-condition warning — without promising a per-kind literal or query plan, per the carried deviation from step
03. The closing paragraph restates what stays the developer's own: no DDL, no verification, the generated column and
its foreign key still hand-written in a migration. The Composite Primary Keys section's claim that key order governs
"the order a relation's foreign keys are matched against the target's key" is corrected to say relation matching is
now pair-based and order-independent, matching `CONTEXT.md`'s **Key order** entry. The Attributes table's `[Relation]`
row, the Requirements list's relation-property bullet, and the Tenancy section's `PGSQL0027` bullet are rewritten to
describe the reshaped, pair-based, direction-free check and the two shapes it now permits by name (the tenancy pair on
both sides, and the per-tenant singleton). The Build-Time Diagnostics table drops `PGSQL0012`, `PGSQL0013` and
`PGSQL0019` and gains `PGSQL0028` through `PGSQL0035`; `PGSQL0016` and `PGSQL0017`'s rows are reworded to match their
actual, reshaped triggers (a relation definition rather than a table definition; no `public` requirement); the
narrative paragraph below the table is updated to name the current set of relation-dropping ids and warnings, and a
closing sentence notes the three retired ids are never reused. The root `README.md`'s relations bullet needed no
change — it names no mechanism the feature made untrue. Neither README gained an ADR link, a changelog, a roadmap, or
a test note. `CONTEXT.md` was checked against what shipped and needed no correction — it already matched.

`AnalyzerReleases.Unshipped.md`'s eight rows moved to a new `## Release 0.37` heading in `AnalyzerReleases.Shipped.md`
(New Rules: `PGSQL0028`–`PGSQL0035`; Removed Rules: `PGSQL0012`, `PGSQL0013`, `PGSQL0019` with their shipped titles
verbatim), the same way the 0.36 release was rolled, and `AnalyzerReleases.Unshipped.md` is left empty, matching that
commit's precedent exactly (checked via `git show` on the 0.36 rollover commit). `Directory.Build.props`'s
`PgSqlVersion` is `0.37.0`.

Verification, run sequentially in the foreground with Docker running:
- `dotnet format` — reformatted nothing beyond the files this step touched; `dotnet format --verify-no-changes` exits 0.
- `dotnet build` (whole solution) — 0 warnings, 0 errors.
- `dotnet test`, run per project (`DOTNET_ROLL_FORWARD=LatestMajor` for the net9.0 projects, the same pre-existing
  environment quirk every prior step in this run noted): Analyzers.Tests 160/160, Tests.Unit 197/197,
  Tests.Integration 263/263 (Docker/Testcontainers), Tests.Integration.OData 134/134, Tests.Packaging 13/13. All green
  — no test needed changing for this step, since it touches only documentation, release tracking and the version
  property.

### Deviations

None from the spec or the step file. The ADR's Consequences section and the README's condition subsection both state,
rather than omit, the two carried deviations from earlier steps (the `Sql.Constant` literal not surviving the
association path, and `PGSQL0017` no longer requiring `public`) exactly as the driving prompt required — this is
documenting prior deviations honestly, not a new one introduced here.

**Corrected by the review pass.** The first of those two was diagnosed wrongly by step 03 and is described wrongly
above. The provider's association path *does* render a constant as a literal; step 03's conclusion that it "does not
route through the visitor that honours `Sql.Constant`" is not what is happening. What actually parameterizes the
comparison is the **value conversion** on the compared column — this library maps every enum with one — and no
wrapper changes that, `Sql.Constant` least of all, which is inert in an association predicate for converted and
unconverted constants alike. `Sql.ToSql`/`Sql.AsSql` do force a literal, and are wrong here because they force it
past the conversion: a kind column stored as text would be compared against `1`. The now-inert `Sql.Constant` wrap
was therefore removed from the generator, the `LinqToDB` stub was dropped from the harness so a reintroduction fails
the "emitted source compiles" guard, ADR 0010 was rewritten to say this accurately, and an integration assertion now
pins the parameter binding so a future provider version changing it fails a test rather than passing unnoticed.
