# Carry column nullability into generated query mappings

Status: ready-for-agent

Promoted from `.agents/ideas/query-column-nullability.md` after a grilling session. Every open question that idea
carried is answered below. The reasoning behind the two decisions that were genuine trade-offs is recorded in
[ADR 0007](../../docs/adr/0007-declared-column-nullability.md).

## Motivation

Generated mappings tell the **Query surface** a column's name and whether it is a key member, and nothing else. The
provider therefore falls back to its own default, which is a pure CLR-type test — a reference type can be null, a value
type cannot — so `string` and `string?` are indistinguishable to it and both are treated as nullable.

Under the provider's null-comparison mode, kept deliberately per
[ADR 0004](../../docs/adr/0004-linq2db-as-the-queryable-provider.md) because it matches both C# and the OData
specification, a nullable column widens every inequality with an "or the column is null" alternative. On a column that
cannot hold null that alternative can never match, and it makes the predicate non-sargable.

The cost is measured, not theoretical. Against PostgreSQL 18 on two 50k-row tables joined on a two-column key with a
matching btree index on both sides, the widened form degrades the composite index to leading-column-only: the second
column moves out of `Index Cond` into `Filter`, index searches rise from 250 to 50,000, shared buffers from 1,516 to
25,116,417, and runtime 232x. On the default planner's hash join the second column is demoted to a `Join Filter` that
removes 24,975,000 rows, costing 54x; on a selective 500-row driving set, 18x. Row counts are identical in every case.

`TenantLinkTable.Kind` in the integration suite is that shape today: a `[PrimaryKey]` member typed non-nullable `string`,
on both sides of a **Relation**'s join condition. `PGSQL0020` lets it through because its rule is about the property's
*type*, and `string` can hold null as far as the type system is concerned.

## Decisions (locked)

### The claim

A **Nullability claim** is nullable unless something says otherwise:

| Property shape                                            | Claim    |
| --------------------------------------------------------- | -------- |
| Non-`Nullable<T>` value type (`long`, `DateOnly`)         | not null |
| `Nullable<T>` (`long?`)                                   | null     |
| Reference type, `NullableAnnotation.NotAnnotated`         | not null |
| Reference type, `NullableAnnotation.Annotated` (`string?`) | null     |
| Reference type, `NullableAnnotation.None` (NRT off)       | null     |
| Any property carrying `[Column(Null = true)]`             | null     |
| Any property carrying `[Column(NotNull = true)]`          | not null |
| Any `[PrimaryKey]` member                                 | not null |

`NullableAnnotation.None` is exactly the nullable-oblivious case, so no `NullableContextOptions` lookup is needed. An
oblivious file makes no claim and keeps today's behaviour.

Neither `[Unique]` nor `[Generated]` implies anything: PostgreSQL permits any number of nulls in a unique index, and a
database-supplied value can perfectly well be null — a stored generated column over a polymorphic discriminator is null
for every kind but its own, which is the case [ADR 0006](../../docs/adr/0006-composite-primary-keys.md) already relies on.

### The API

- `ColumnAttribute` gains a parameterless constructor and two settable bool properties, `Null` and `NotNull`, both
  defaulting to false. Its summary widens from naming the column to stating column facts. No new attribute type: `Null`
  and `NotNull` as standalone attributes would collide with `System.Diagnostics.CodeAnalysis.NotNullAttribute` and
  `JetBrains.Annotations.NotNullAttribute`, both of which allow `AttributeTargets.Property`, and a **Table definition**
  file must already import this library's namespace for `[Table]` and `[PrimaryKey]` — so any file importing either of
  those alongside it would get CS0104.
- `QueryEntityMappingBuilder<TEntity>.Column` gains a fourth optional parameter, `bool isNotNull = false`, and calls
  `IsNotNull()` on the property builder when it is set. Source-compatible, binary-breaking.
- The builder, not the generator, applies the key rule: `if (isPrimaryKey) propertyBuilder.IsNotNull()`. The builder is
  public surface a consumer calls by hand, so the rule holds for every caller and is stated once in the shipped library.
  Emitted code for key columns is unchanged.
- Only the not-null direction is ever emitted. Nullable is already the provider's default wherever the type can express
  it, so a nullable column needs no argument.

### Diagnostics

One new descriptor, `PGSQL0021`, error severity, reported once per offending property with a message naming which
contradiction it is:

- `NotNull = true` on a property whose type can hold null (`long?`, `string?`)
- `Null = true` on a non-nullable value type
- `Null = true` and `NotNull = true` together
- `Null = true` on a `[PrimaryKey]` member

It abandons nothing. The claim is dropped and the column falls back to whatever its type and key membership already
settle, so the consumer sees one error rather than a cascade of missing-type errors across their own code. This differs
from `PGSQL0020`, which abandons the table because a malformed key leaves every generated signature undefined; a
contradictory claim leaves them all well-defined.

A not-null claim on a reference type in a nullable-oblivious file is *not* a contradiction — the annotation that would
carry a claim cannot be written there, so the attribute is the only thing said about the column, and that is the case it
exists for.

Redundant-but-true claims stay silent: `NotNull = true` on a key member, or `Null = true` on a property already typed
`string?`, restates something true and earns no diagnostic. `PGSQL0020` is unchanged.

## What this wins, and what it does not

Predicates on a driving table's own columns lose the `OR col IS NULL` alternative, and so do join `ON` conditions —
which is what closes the `TenantLinkTable.Kind` case.

A filter reaching *across* a **Relation** stays widened whatever the claim says. The provider widens a predicate when
the column's *table* is the nullable side of an outer join, independently of the column's own flag, and every Relation
is an outer join by contract per [ADR 0005](../../docs/adr/0005-table-relations-on-relation-properties.md). So
`x.Author!.Name != "bob"` is unimproved. Integration assertions therefore have to target the driving table's own columns
and join `ON` conditions.

## Risk accepted

The claim is never verified against the real table. A column claimed not-null that actually holds null fails when the
row is read, not silently: setting the provider's column nullability to false omits the `IsDBNull` guard from the
generated reader. That is the same trade the library already makes for column names, for composite keys and for
generated columns, and the failure is loud rather than quiet. Schema verification in the `db` tool was considered and
deliberately deferred — see `.agents/ideas/verify-definitions-against-schema.md`.

## Work

1. `ColumnAttribute`: parameterless constructor, `Null` and `NotNull` properties, widened summary.
2. `QueryEntityMappingBuilder<TEntity>.Column`: fourth `isNotNull` parameter; `IsNotNull()` when set or when
   `isPrimaryKey` is set.
3. `TableDefinitionSymbols`: read `Null`/`NotNull` off `[Column]`; add the claim to `PropertyDefinitionModel` as a
   notion distinct from the existing `IsNullable`, which keeps its `PGSQL0020` meaning.
4. `TableDefinitionParser`: report `PGSQL0021`; drop a contradictory claim.
5. `TableRepositoryDiagnostics`: the `PGSQL0021` descriptor.
6. `GeneratedAssemblyRegistrationSourceBuilder`: emit `isNotNull: true` for a claimed non-key column.
7. `GeneratorHarness.RUNTIME_STUBS`: mirror the new `Column` signature and the `[Column]` properties — the stub, not the
   real assembly, is what generator tests compile against.
8. Tests, below.
9. `README.md`; `<PgSqlVersion>` 0.33.0 → 0.34.0 in `Directory.Build.props`.

## Tests

Generator (`mvdmio.Database.PgSQL.Analyzers.Tests`):

- Emitted `isNotNull:` argument per property shape, across the whole table above.
- `PGSQL0021` for each of the four contradictions; error severity; generation continues and the column is nullable.
- The oblivious case: a reference-typed column in a nullable-disabled compilation emits no claim. Needs a
  `NullableContextOptions.Disable` path in `GeneratorHarness`, which today compiles NRT-on only.

Integration (`mvdmio.Database.PgSQL.Tests.Integration`):

- `!=` on a non-nullable `string` on the driving table renders no `IS NULL`, against the existing `string?` pin that
  requires one.
- `Query_ReachingACompositeRelation_ConstrainsEveryKeyColumnWithPlainEquality` extended to the `Kind` key member.
- The false-claim failure mode: DDL permitting null under a property claiming not-null fails when the row is read. This
  pins provider behaviour the ADR's risk paragraph depends on, which could change under a linq2db upgrade.

## Out of scope

- Changing the null-comparison mode. Settled by ADR 0004 and specification-correct.
- Inferring nullability from the database instead of from the **Table definition**.
- Verifying claims against a pulled schema — its own idea, deliberately deferred.
- Mapping `[Generated]` onto the provider's identity flag. It would imply not-null, but it also changes insert
  behaviour.
- `Configuration.UseNullableTypesMetadata`. Rejected; see ADR 0007.
