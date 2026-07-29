---
status: accepted
---

# Let a column claim how it is stored, apply the claim where the value is bound, and store enums as text by default

A **Table definition** could name a column and state its **nullability claim**, but nothing about how the value is
represented. Three failures followed from the one gap. An enum was written as text by the Dapper type handler this
library ships and read as an integer by the **Query surface**, because the two keep separate conversion registries and
only one of them knew about enums — so a definition that worked through `CreateAsync` could not be filtered through
`Query()`. A `string` mapped to a `jsonb` column compiled clean, passed the analyzer, and failed every write with
`42804`, because the generated `INSERT` bound the parameter with no type and Npgsql inferred `text`. And `sbyte`,
`ushort`, `uint` and `ulong` were admitted by the mappable-type allowlist and rejected by the driver at run time.

We decided that a **Table definition** declares each column's **storage claim** through `[Column(StoredAs = …)]`,
spelled with Npgsql's own `NpgsqlDbType`; that the claim is per column rather than per type; that it is applied where
the generated code binds the value rather than through a registry; that an unclaimed enum is stored as the text of its
member name; and that the claim is permitted unless its failure has been demonstrated, rather than restricted to what
has been proven to work.

## Considered options

**Where the claim lives:**

- **Per column, on `[Column]` (chosen).** The same enum can be stored as text on one table and as an integer on
  another, and each column says which. Widening `[Column]` from naming a column to stating facts about it is the road
  [ADR 0007](0007-declared-column-nullability.md) already took for the nullability claim, so the two claims read alike
  and sit in one place. "Unclaimed" needs no sentinel: an attribute property cannot be `Nullable<TEnum>`, but the
  analyzer reads source, and the absence of `StoredAs` from the attribute's named arguments is the absence of a claim —
  exactly how `Null` and `NotNull` are already told apart from their `false` defaults.
- **Per CLR type, through the existing registries.** Rejected, and it is the option a future reader will reach for,
  because both surfaces already have one: `SqlMapper.AddTypeHandler` on the Dapper side, `MappingSchema.SetConverter`
  and `MapValueAttribute`/`SetDefaultFromEnumType` on the provider side. All of them are keyed by type and
  process-wide, which is precisely the shape that produced the enum divergence — two registries, one truth, kept in
  step by hand. A per-type mechanism also cannot express two columns of the same enum stored differently, and the
  provider offers no per-column route through `MapValue` at all, only through a per-member conversion.

**How the claim is spelled:**

- **Npgsql's `NpgsqlDbType` (chosen).** The claim is what the parameter is bound as, so on the Dapper surface claim and
  wire representation are the same value and there is nothing to translate. Only the provider needs a translation
  table, and only for the members it can represent. Npgsql is already a public, non-private dependency of the package,
  so this adds no coupling that consumers do not already have.
- **A library-owned `PgType` enum.** Rejected. It would have made the implemented set self-evident from the type — the
  real merit, and the reason it was close — but it needs two translation tables instead of one, and the closed set it
  advertises is no longer what we ship (see below).
- **A free-form string, `StoredAs = "jsonb"`.** Rejected: the analyzer cannot validate it and the library cannot act on
  it.

**How the claim reaches the wire:**

- **At the binding site, in generated code (chosen).** An enum claimed as text binds `value.ToString()`; a `string`
  claimed as `Jsonb` binds through `TypedQueryParameter`, the `ICustomQueryParameter` this library already shipped and
  never used. Nothing is registered, so nothing can be registered twice or disagree. It also makes the claim reachable
  for a type the driver refuses to infer for: `sbyte` fails today only because Dapper infers `DbType.SByte`, which
  Npgsql has no mapping for, and binding `NpgsqlDbType.Smallint` explicitly makes it work.
- **A cast in the emitted SQL, `VALUES (:Content::jsonb)`.** Rejected, though it is what the driving application writes
  by hand today and it shows up plainly in a SQL log. It states the claim a second time, in a place that can drift from
  the mapping, and it does nothing for the **Query surface**, which needs the claim as provider metadata either way.

**What an unclaimed enum is stored as:**

- **Text (chosen).** It is what this library's own `EnumAsStringTypeHandler` and README already promise, what the
  driving application's 48 enums and their `CHECK` constraints already contain, and the representation that survives
  inserting a member in the middle of a declaration. Reading is free: Dapper converts a `text` column to an enum
  natively and case-insensitively with no handler registered at all, so only the write path needs the conversion.
- **The underlying integer, with text as the claim.** Rejected. It is the provider's default and today's behaviour on
  the query side, so it would break nobody — but it makes the common case the annotated one, and it contradicts the
  documented promise rather than fulfilling it.

**Which claims are refused:**

- **Only those demonstrated to fail (chosen).** `ushort`, `uint` and `ulong` are refused as column types outright,
  because Npgsql registers no integer or numeric mapping for any of them — not by inference and not with an explicit
  `NpgsqlDbType`. Every other combination is permitted, and joins the refused set only when a test in this repository
  demonstrates its failure.
- **An allowlist of the combinations the library implements.** Rejected. It is the safer default and it would turn every
  unimplemented claim into a build error instead of a run-time one — but the list would be a hand-maintained mirror of
  Npgsql's internal mapping tables, which is the same drift this ADR exists to remove, and it would refuse
  combinations that work for no reason beyond our not having tested them.

## Consequences

- **The two surfaces can no longer disagree about a column, because only one place decides.** The claim is read once,
  from the definition, and emitted into both the parameter binding and the provider mapping. This retires the enum, JSON
  and numeric entries from the analyzer's hand-kept mirror of a registry it cannot reference; the `Uri` and
  `Dictionary<string, string>` entries remain, because those conversions stay process-wide.
- **A permitted claim is not a supported claim, and the build says so where it can.** `PGSQL0022` refuses a combination
  known to fail and starts nearly empty by design; a future reader should not read that emptiness as unfinished work.
  `PGSQL0024` warns where a claim has no provider representation — `Inet`, `Cidr`, the geometry types — because such a
  column is honoured on the Dapper surface and ignored on the **Query surface**, which is the divergence this ADR
  removes elsewhere and cannot remove there. Everything outside both is permitted, untested, and documented as such.
- **The enum default is the one behaviour change that can break an existing consumer.** A consumer hand-registering
  mappings and never calling `AddEnumDapperTypeHandlers` gets integers on both surfaces today and text on both after.
  It breaks loudly rather than silently — `operator does not exist: integer = text` — and the generated-repository
  surface has no consumers to break at all, because until this release the generator was never packed into the
  published package. MINOR bump under the project's pre-1.0 rule.
- **Reading an enum is case-insensitive on both surfaces, deliberately.** Dapper's native `text`-to-enum path parses
  with `ignoreCase: true`, so the provider-side conversion matches it rather than being stricter. Two members differing
  only in case are legal C# and will throw at parse time on either surface; that is not diagnosed.
- **`AddEnumDapperTypeHandlers` survives without being load-bearing.** Generated repositories no longer need it, and it
  cannot conflict with a claim in either direction — a text-claimed column binds a `string` and an integer-claimed one
  binds an `int`, so the per-type handler never fires on the write path. It stays for hand-written Dapper SQL, which
  the driving application depends on throughout a fifty-four-table migration.
- **A false claim behaves like a false nullability claim: taken on trust, and paid for at run time.** The claim joins
  column names, composite keys, generated columns and nullability as something the library takes the definition's word
  for. Checking a definition against a pulled schema is deliberately not attempted, and this ADR enlarges what such a
  check would be worth.
