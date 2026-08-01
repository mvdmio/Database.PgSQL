# 03 — Record the rule and ship it

Status: done

The last step of this spec, so it also carries the version bump the project requires of any library change.

## What to build

The documents catch up with the rule that now ships, and the package version says the build changed.

**A new ADR, `docs/adr/0011-…`.** It records why ADR 0010's refusal was replaced, so a future reader finds the reasoning
rather than rediscovering the measurement behind it. What it has to carry:

- The rule reads the pair, not the target column: a **Relation key** is refused when both of its columns can hold null,
  and uniqueness stopped being an input.
- ADR 0010's premise was false. One same-type `Key(...)` overload already accepts every nullability combination,
  because the type argument is inferred from both lambdas at once and settles on whichever of the two types the other
  converts to. No arrangement of overloads could make a pairing a compile error, so the analyzer is the only place the
  rule can live.
- Why the refused shape is worth refusing: the query provider widens a comparison between two nullable columns into
  "equal, or both are null", which joins every null on one side to every null on the other and loses the index — ADR
  0006 measured 232x on a nested loop and 54x on a hash join over two fifty-thousand row tables.
- Considered and rejected, so nobody re-derives them: a **Relation condition** excluding nulls (it removes the wrong
  rows but not the widening, so the index is still lost — deliberately unlike `PGSQL0031`, which a condition *can*
  rescue, because there the condition supplies the missing guarantee outright); the whole-context null-comparison mode
  (the only lever is context-wide, ADR 0004 chose the current mode deliberately and the OData front-end depends on it);
  and reaching into a **Relation condition**'s own comparisons (judging expression shapes would start permitting only
  the shapes the library recognises, the opposite of how the **Translation boundary** is drawn everywhere else).
- The sharp edge, stated plainly: where two columns are genuinely nullable and neither side can honestly be claimed
  otherwise, the relation cannot be declared at all. If that blocks a real schema, the answer is a new decision about
  how to compare two nullable columns, not a quiet exception to this one.
- The consequences: `PGSQL0035` keeps its id with a new meaning, severity and blast radius unchanged, `PGSQL0020` still
  reads the C# type, `RelationDefinition<,>` untouched, and the version bump.

**ADR 0010 stays as written and gains a pointer forward** — the same admonition-block pattern ADR 0005 and ADR 0006 use
to point at ADR 0010. Its "The nullable-target-side question is settled by refusal, not by a third overload"
consequence is the part now corrected; the record of why refusal looked right at the time survives alongside the
correction.

**`CONTEXT.md`.** The **Relation key** and **Nullability claim** entries describe the rule that ships: a **Relation
key** is refused when both its columns can hold null, and it reads the **Nullability claim** rather than the property's
C# type — so the two terms agree with the build.

**`README.md` and `src/mvdmio.Database.PgSQL/README.md`.** The existing paragraph about a column's nullability gains
one sentence: a **Relation key** reads the same claim, and a pair whose two columns can both hold null is refused.
Relations are not otherwise shown in the root README and this change does not add them. In the library README, the
diagnostics table's `PGSQL0035` row and the prose around `Key(...)`'s two overloads and the cardinality-and-uniqueness
claim have to agree with what now ships — including that the refusal is no longer about `[Unique]` at all.

**`<PgSqlVersion>` goes 0.37.0 → 0.38.0.** No public API changes, but the diagnostic change breaks builds that pass
today, which is a MINOR bump under this project's pre-1.0 rule.

## Footprint

Projects: `src/mvdmio.Database.PgSQL`, `src/mvdmio.Database.PgSQL.Analyzers`, `src/mvdmio.Database.PgSQL.Tool` — every
project reads `<PgSqlVersion>`, so the whole solution must build and the whole suite must stay green after the bump
(`test/mvdmio.Database.PgSQL.Tests.Packaging` packs under a run-unique version and is the one most likely to notice).

- `docs/adr/0011-<slug>.md` — new; `status: accepted` front matter, matching the shape of `0007`–`0010`
- `docs/adr/0010-relation-definitions.md` — the pointer-forward block at the top, and the "nullable-target-side
  question is settled by refusal" consequence
- `docs/adr/0005-table-relations-on-relation-properties.md`,
  `docs/adr/0006-composite-primary-keys.md` — prior art for the pointer-forward block only
- `CONTEXT.md` — the **Relation key** and **Nullability claim** entries
- `README.md` — the nullability paragraph (around "A property's type also tells the query surface…")
- `src/mvdmio.Database.PgSQL/README.md` — the `### Column Nullability` section, the "Stating the pairs" and "The
  cardinality and uniqueness claim" paragraphs under relations, the diagnostics table row for `PGSQL0035`, and the
  prose below that table about which diagnostics drop only the relation
- `Directory.Build.props` — `<PgSqlVersion>`

## Acceptance criteria

- [ ] `docs/adr/0011-…` exists, is `status: accepted`, and records the decision, the corrected ADR 0010 premise, the
      rejected alternatives (relation condition, context-wide comparison mode, condition comparisons) and the
      consequences
- [ ] ADR 0010 is otherwise unedited and carries a pointer forward to ADR 0011 in the same style ADR 0005 and ADR 0006
      point at ADR 0010
- [ ] `CONTEXT.md`'s **Relation key** and **Nullability claim** entries describe the rule that ships
- [ ] Both READMEs' nullability paragraphs say a **Relation key** reads the same claim and that two nullable sides are
      refused
- [ ] The library README's `PGSQL0035` row and surrounding relation prose match the shipped rule, with no leftover
      claim that a nullable `[Unique]` target is refused or that a third `Key(...)` overload was ever needed
- [ ] Neither README gains an ADR link, a changelog, a roadmap or a test note
- [ ] `<PgSqlVersion>` is `0.38.0`
- [ ] `dotnet format --verify-no-changes` exits zero, `dotnet build` is clean, and `dotnet test` is green across the
      solution

## Outcome

`docs/adr/0011-relation-key-pairs-refused-by-nullability-not-uniqueness.md` is new, `status: accepted`, matching the
front matter shape of `0007`–`0010`. It records: the corrected premise (uniqueness never mattered, ADR 0010's "third
overload" reasoning was false and is settled by compiling every nullability combination against the one same-type
`Key(…)` overload), the decision (the rule reads whether both columns' **Nullability claim** can hold null, not
whether either is `[Unique]`), the three rejected alternatives named in the spec (a null-excluding **Relation
condition**, the context-wide null-comparison mode, and reaching into a condition's own comparisons), and the
consequences (`PGSQL0035` keeps its id/severity/blast radius with a new message, `PGSQL0020` and `RelationDefinition<,>`
untouched, the sharp edge stated plainly, and the version bump).

`docs/adr/0010-relation-definitions.md` gained a pointer-forward admonition block at the top, in the same style ADR
0005 and ADR 0006 use to point at ADR 0010 — quoting which of its own consequences ADR 0011 corrects and stating that
everything else it decided still stands. The ADR's body is otherwise unedited.

`CONTEXT.md`: the **Relation key** entry gained a sentence stating the both-nullable refusal and that it applies
regardless of uniqueness; the **Nullability claim** entry gained a sentence pointing out that a **Relation key** reads
this claim rather than a property's C# type, naming the one place the two disagree in practice.

`README.md`'s existing nullability paragraph gained one sentence: a relation key reads the same claim, and pairing two
columns that can both hold null is refused. No relations were otherwise added to the root README.

`src/mvdmio.Database.PgSQL/README.md`: the Column Nullability section gained a pointer sentence to `PGSQL0035`; the
"Stating the pairs" paragraph now states that the same-type `Key(…)` overload alone accepts every nullability
combination (with the worked example) and points at `PGSQL0035` for the one thing a pair may not do, rather than
implying the two overloads exist to route nullable-vs-non-nullable combinations; the "cardinality and uniqueness
claim" paragraph gained a clause that a nullable `[Unique]` column still counts as unique, since PostgreSQL admits any
number of nulls under a unique constraint; the diagnostics table's `PGSQL0035` row now reads "A relation key pair's two
columns can both hold null", with no leftover claim that a nullable `[Unique]` target is refused or that a third
`Key(…)` overload was ever needed.

`Directory.Build.props`'s `<PgSqlVersion>` is `0.38.0`.

Deviations from the footprint: none of substance. The footprint listed the "Stating the pairs" prose as needing to
agree with what ships; the two `Key(…)` overloads themselves are unchanged (confirmed against
`src/mvdmio.Database.PgSQL/Relations/RelationDefinition.cs`), so the edit there clarifies the same-type overload's
reach rather than describing new signatures. No other file outside the footprint needed touching, and
`AnalyzerReleases.Unshipped.md` was checked again and remains empty.

`dotnet format --verify-no-changes` exits zero, `dotnet build` is clean (0 warnings, 0 errors) across the whole
solution, and `dotnet test` (via `DOTNET_ROLL_FORWARD=LatestMajor`, per this environment's net9.0 quirk) is green: 780
tests passed, 0 failed, across `mvdmio.Database.PgSQL.Tests.Unit` (197), `.Integration` (268), `.Integration.OData`
(134), `.Analyzers.Tests` (168) and `.Tests.Packaging` (13). Docker was running for the Testcontainers-backed
integration suite.
