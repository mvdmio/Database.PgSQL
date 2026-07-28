---
status: accepted
---

# Take a column's nullability from the Table definition, defaulting to nullable, and state it in the mapping

A generated mapping tells the **Query surface** a column's name and whether it is a key member. Nullability it does not
state, so the provider falls back to its own default, which is a pure CLR-type test: a value type cannot be null, a
reference type can. `string` and `string?` are therefore indistinguishable to it and both are treated as nullable, and
under the null-comparison mode [ADR 0004](0004-linq2db-as-the-queryable-provider.md) keeps deliberately, a nullable
column widens every inequality with an "or the column is null" alternative that on a non-nullable column can never
match and costs the predicate its index.

We decided that a **Table definition** declares each column's **nullability claim**; that the claim is nullable unless
something says otherwise, matching PostgreSQL's own column default; that C# states the claim wherever the language can
express one, with `[Column(Null = …)]` / `[Column(NotNull = …)]` overriding it; that a `[PrimaryKey]` member is never
nullable; and that the mapping builder states the not-null case to the provider explicitly.

## Considered options

**How the claim is stated:**

- **The property's own type, with attributes overriding it (chosen).** `long` and `string` are not null, `long?` and
  `string?` are null, a reference type in a nullable-oblivious file states nothing and is nullable. Every column that is
  already honestly annotated — which is every column in a project with nullable reference types on — gets the narrower
  SQL with no edit. It keeps the two facts in one place: a reader of the definition sees the claim in the type they were
  already reading, and `[Column]` is there for the cases the type cannot carry.
- **Attributes as the only mechanism, the type being consulted only where it makes null impossible.** Rejected, though
  it was close and it has the cleaner story: the library's default, the provider's default and PostgreSQL's default
  would be one and the same, one mechanism, immune to a consumer's nullable setting. It was rejected because the win
  becomes opt-in per column — the driving application would hand-annotate every NOT NULL text column across
  fifty-nine tables to get what its existing annotations already say — and because it makes `?` on a reference-typed
  column mean nothing to the mapping while `PGSQL0020` still reads it as meaningful on a key member.
- **The provider's own `Configuration.UseNullableTypesMetadata`.** Rejected, and it is the option a future reader is
  most likely to reach for, because it would deliver the same reading of C# with no attribute and no mapping change at
  all. Three reasons against: it is a process-wide static, which a library has no business setting on its consumer's
  behalf; it throws when nullable metadata has been trimmed, which is the default for some client application models;
  and the same flag also switches **Relation** join types to nullability-derived, which contradicts the always-an-outer-join
  contract stated on the mapping builder.

**Where the override is expressed:**

- **Named arguments on the existing `[Column]` attribute (chosen).** `[Column(NotNull = true)]`, or
  `[Column("first_name", NotNull = true)]`. Two bool properties rather than one, because the claim is tri-state — stated
  null, stated not-null, unstated — and C# does not admit `bool?` as an attribute argument type. This adds no new type
  to the public surface and widens `[Column]` from naming a column to stating facts about it.
- **Standalone `[Null]` and `[NotNull]` attributes.** Rejected on a name collision that is worse than it first looks.
  Both `System.Diagnostics.CodeAnalysis.NotNullAttribute` and `JetBrains.Annotations.NotNullAttribute` allow
  `AttributeTargets.Property`, and a Table definition file must already import this library's namespace for `[Table]`
  and `[PrimaryKey]` — so any file importing either of those alongside it gets CS0104 on `[NotNull]`, unfixable without
  an alias. Every other attribute this library ships is collision-free; this would have been the first that is not.
- **An enum-valued property.** Rejected: it makes the tri-state explicit and both-directions-at-once unrepresentable, at
  roughly triple the width at every call site, for a contradiction that is diagnosed anyway.

**Who applies "a key member is not null":**

- **The mapping builder (chosen).** ADR 0006 established that the builder is public surface a consumer calls by hand, so
  putting the rule there makes it true of every caller and states it once in the shipped library. Generated code for key
  columns does not change.
- **The generator, emitting the flag alongside the key flag.** Rejected: redundant in the emitted source, and a
  hand-written call that omitted it would silently lose the join-condition improvement, which is the exact bug this ADR
  exists to fix.

## Consequences

- **The improvement reaches a driving table's own predicates and join `ON` conditions, and stops there.** The provider
  widens a predicate when the column's *table* is the nullable side of an outer join, independently of the column's own
  nullability, and every **Relation** is an outer join by contract per
  [ADR 0005](0005-table-relations-on-relation-properties.md). A filter reaching across a Relation is therefore
  unimproved and always will be, short of revisiting that contract. Assertions about the narrower SQL have to target the
  driving table's own columns and join conditions.
- **This closes a hole in ADR 0006's guarantee rather than merely extending it.** That decision refuses a nullable key
  member and reasoned that this makes the widened join condition unreachable by construction. Its rule is about the
  property's *type*, so a key member typed non-nullable `string` passes it and is still widened, because the provider
  reads `string` as nullable. `TenantLinkTable.Kind` in the integration suite is that shape. The guarantee holds only
  once the claim is stated in the mapping.
- **A false claim fails loudly when the row is read.** Setting a column non-nullable also omits the `IsDBNull` guard
  from the provider's generated reader, so a column claimed not-null that actually holds null throws rather than
  quietly returning wrong rows. That is a better failure than the alternative and it is the reason no verification is
  built: the claim joins column names, composite keys and generated columns as something the library takes the
  definition's word for. Verifying definitions against a pulled schema is a separate, deliberately deferred idea.
- **A nullable-oblivious consumer is unaffected.** A reference type whose nullability annotation is absent makes no
  claim, so a project with nullable reference types switched off keeps exactly today's SQL and today's plans. This is
  read from the annotation state alone; no compilation-level nullable setting is consulted.
- **One diagnostic is added, and it does not abandon the table.** `PGSQL0021` is an error, reported per offending
  property, covering a not-null claim on a type that can hold null, a null claim on a type that cannot, both claims at
  once, and a null claim on a key member. The claim is dropped and the column falls back to whatever its type and key
  membership already settle. Unlike
  `PGSQL0020`, which abandons the table because a malformed key leaves every generated signature undefined, a
  contradictory claim leaves them all well-defined, so abandoning would bury the one real error under a cascade of
  missing-type errors in the consumer's own code. A redundant but true claim is not diagnosed.
- **API surface.** `[Column]` gains a parameterless constructor and two properties; the mapping builder's `Column`
  method gains a fourth optional parameter, which is source-compatible and binary-breaking in the same way ADR 0006's
  variadic `[Relation]` was. MINOR bump under the project's pre-1.0 rule.
