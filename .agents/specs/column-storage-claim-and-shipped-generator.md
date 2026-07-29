# Ship the generator, and let a column claim how it is stored

Status: ready-for-agent

## Problem Statement

An application converting its whole data layer to this library's generated repositories cannot adopt them. Four
separate defects block it, and it cannot adopt partially — its generated `*Data` types are meant to become its canonical
row types and replace its hand-written records, so a surface that works for half its tables is worth nothing.

1. **The source generator is not in the published package.** Installing `mvdmio.Database.PgSQL` from NuGet resolves the
   attributes — they are ordinary classes in the main assembly — but the generator never runs. `[Table] partial class
   XTable` produces nothing, and the README's own "Using the Repository" sample does not compile. Verified against the
   real published artifact and a local release build: both contain `build/`, `buildTransitive/`, `lib/net{8,9,10}.0/`,
   README, LICENSE and nuspec, and no `analyzers/` folder. The analyzer has never been published as its own package
   either. Nothing in this repository ever consumes the packed artifact, so CI is blind to it — every test project
   references the analyzer project directly and separately.

2. **An enum round-trips as text through the Dapper surface and as an integer through the Query surface.** The two keep
   separate conversion registries and only one of them knows about enums. An application storing every enum as `text`
   therefore writes `'Open'` through `CreateAsync` and asks for `= 2` through `Query().Where(x => x.State ==
   TaskState.Open)`, against the same column. There is no application-side workaround: a **storage claim** could not be
   stated anywhere.

3. **A `jsonb` column compiles clean and fails every write.** The generated `INSERT` binds every value as a bare named
   parameter with no type, whatever the property's type. A `string` mapped to `jsonb` is sent as `text`, and PostgreSQL
   rejects it with `42804: column "x" is of type jsonb but expression is of type text` — there is no implicit cast.
   `string` is a mappable type, so no diagnostic fires: this passes the analyzer and then throws on every call.

4. **A `[Generated]` column cannot keep a private setter.** The property-shape rule requires a public setter and its
   diagnostic is an error, so `public DateTime CreatedAt { get; private set; }` abandons the whole table. The driving
   application declares 38 `CreatedAt` and 29 `LastUpdatedAt` properties that way; the shape is its idiom for
   database-populated columns, paired with `required … { get; init; }` for caller-supplied ones. The rule bites hardest
   where it is least justified — a generated column is never written by the caller and is excluded from every write
   command by design.

Alongside them, a documented and unfixed hole in the same code path: `sbyte`, `ushort`, `uint` and `ulong` are admitted
as column types, can be read and filtered, and cannot be written. Every insert and update on such a table throws.

## Solution

One release, `0.35.0`, containing all of it. The generated-repository surface has no package consumers to fragment —
that is what defect 1 establishes — so there is no audience for an incremental rollout, and the driving application
jumps from `0.29.0` straight to this.

**The generator ships inside the main package.** It goes to `analyzers/dotnet/cs` in `mvdmio.Database.PgSQL` rather than
becoming a package of its own, because generated code calls into the library's own API and the two must version-lock;
two packages would make skew expressible. Its Roslyn reference drops to the oldest version that can be loaded by any
SDK able to target the library's oldest framework. A test installs the produced `.nupkg` into a throwaway project and
asserts a generated repository compiles and runs, so this cannot regress silently.

**A column may claim how it is stored.** `[Column(StoredAs = …)]` states a **storage claim**, spelled with Npgsql's own
type enum, and the claim is applied where the generated code binds the value — not through a registry. That single
declaration feeds both the Dapper binding and the Query surface mapping, so the two can no longer disagree about a
column. An unclaimed enum is stored as the text of its member name, which is what this library already promises and what
the driving application's data already contains. A `string` claimed as `jsonb` binds with the right parameter type
instead of being sent as text. `sbyte` starts working, because binding an explicit type is exactly what it needed.

**A column's setter accessibility stops mattering**, because it never mattered: the table definition class is purely
declarative and is never instantiated. `{ get; private set; }`, `{ get; init; }` and `{ get; protected set; }` all
become legal columns, and the generated data type mirrors `[Generated]` columns as `{ get; private set; }` so the
application keeps the encapsulation on the type that replaces its hand-written records.

**Three of the four unwritable numeric types are refused at build time** instead of at run time, and the fourth is
fixed.

## User Stories

1. As an application developer, I want `dotnet add package mvdmio.Database.PgSQL` to give me a working source generator, so that a `[Table]` class produces a repository without my having to discover that the generator ships nowhere.
2. As an application developer, I want the README's repository sample to compile against the published package, so that the documentation is a thing I can trust rather than a thing I have to test.
3. As an application developer building with an SDK too old to load the generator, I want the minimum SDK stated in the README, so that I can recognise "no generated types" as a version problem rather than as my mistake.
4. As a maintainer, I want a test that installs the produced package into a throwaway project and runs a generated repository against a real database, so that "the generator is not in the package" can never ship twice.
5. As a maintainer, I want the packed artifact exercised in CI, so that the guarantee holds on the machine that publishes rather than only on mine.
6. As an application developer, I want an enum column to be filtered through `Query()` the same way it is written through `CreateAsync`, so that my repository is usable end to end.
7. As an application developer storing enums as `text`, I want that to be the default, so that 48 enums need no annotation.
8. As an application developer storing an enum as an integer, I want to say so on the column, so that the default does not force me to change my schema.
9. As an application developer, I want two columns of the same enum type to be storable differently, so that one table's choice does not constrain another's.
10. As an application developer, I want an enum read back identically through both surfaces, so that a value that materialises through a lookup cannot throw through a query.
11. As an application developer whose enum column holds `NULL`, I want it to read back as null rather than throwing, so that a nullable enum column is usable at all.
12. As an application developer, I want a `string` property on a `jsonb` column to insert and update successfully, so that I do not have to keep a hand-written repository for the four columns that hold arbitrary JSON.
13. As an application developer, I want the same `string` property to read back as a string, so that the round trip is symmetrical.
14. As an application developer with JSON held in a `text` column, I want an unclaimed `string` to keep binding as text, so that fixing `jsonb` does not break the columns that were already fine.
15. As an application developer, I want `Dictionary<string, string>` to keep mapping to `jsonb` with no annotation, so that the one JSON shape that already worked continues to.
16. As an application developer, I want `public DateTime CreatedAt { get; private set; }` to be a legal column, so that 45 row types do not have to surrender their encapsulation to be adopted.
17. As an application developer, I want `required X { get; init; }` to be a legal column, so that my caller-supplied properties keep the shape that expresses it.
18. As an application developer, I want the generated data type to keep `[Generated]` columns non-publicly-settable, so that the type replacing my hand-written record does not let callers assign `CreatedAt`.
19. As an application developer, I want a computed, get-only member on a table definition to stay refused, so that relaxing the setter rule does not silently turn an expression-bodied property into a column that does not exist.
20. As an application developer, I want an `sbyte` column to be writable, so that a type the library says it supports actually works.
21. As an application developer using `ushort`, `uint` or `ulong`, I want the build to refuse it, so that I find out at compile time rather than on every insert in production.
22. As an application developer, I want a storage claim the library cannot honour on the Query surface to warn at build time, so that a column that works through Dapper and not through `Query()` is visible before I ship it.
23. As an application developer, I want a storage claim demonstrated to fail to be an error, so that the analyzer's refusals are grounded in evidence rather than in what happened to be tested.
24. As an application developer, I want a storage claim outside the tested set to be permitted, so that the library does not refuse combinations that work purely because nobody wrote a test.
25. As an application developer, I want the README to name the combinations the library actually tests, so that "permitted" and "supported" are distinguishable.
26. As an application developer already storing enums as integers through the Query surface, I want the change of default to fail loudly, so that I discover it in a test run rather than through corrupted data.
27. As an application developer with hand-written Dapper SQL alongside generated repositories, I want the existing enum type-handler registration to keep working unchanged, so that a fifty-four-table migration can proceed table by table.
28. As an application developer, I want `db.Dapper` and `Query()` to behave the same regardless of whether I registered those handlers, so that an opt-in convenience cannot change what a generated repository does.
29. As an application developer, I want a diagnostic that refuses something to name the legal alternatives, so that the error tells me what to write.
30. As a maintainer, I want the enum, JSON and numeric parts of the analyzer's type knowledge to stop being a hand-kept mirror of a registry it cannot see, so that at least those cannot drift apart again.
31. As a maintainer, I want every diagnostic this release ships to appear in the analyzer release-tracking file, so that the record reflects that the rules are now actually shipped.
32. As a maintainer, I want the storage claim recorded as a domain term and an ADR, so that a future reader finds the reasoning rather than reconstructing it.
33. As a maintainer, I want the retired idea file removed, so that a filed problem and its fix do not sit side by side.
34. As a maintainer, I want the two data-source construction paths to agree about Npgsql's dynamic JSON setting, so that which constructor a consumer used stops changing JSON behaviour.
35. As an application developer, I want the OData conformance suite's integer-stored enum to keep working, so that the change of default is proven not to break an explicit claim.
36. As an application developer, I want a single upgrade to `0.35.0` to deliver all of this, so that I pin once instead of four times.

## Implementation Decisions

### Packaging

- The generator ships in `mvdmio.Database.PgSQL` at `analyzers/dotnet/cs`, added by the main project during pack.
  It is added **once**, not per target framework — the package targets three, and a per-framework item would produce
  duplicate-file warnings.
- The analyzer stays a separate project and is not published on its own. Its version follows the shared product version
  rather than a hardcoded one, and its standalone-package metadata (`DevelopmentDependency`, its own analyzer-path pack
  item) is removed, since a standalone package is now a decision against.
- `Microsoft.CodeAnalysis.CSharp` drops from `4.14.0` to `4.8.0`. `4.14.0` requires SDK `9.0.3xx` or newer and will not
  load on any .NET 8 SDK; `4.8.0` corresponds to SDK `8.0.1xx`, the oldest SDK that can target the library's oldest
  framework. `4.3.1` is the true floor — `ForAttributeWithMetadataName` is the only version-gated API in the analyzer —
  but the SDKs it would additionally reach cannot target `net8.0`, so the reach is worthless. Nothing else in the
  analyzer uses an API newer than Roslyn 3.x, and the analyzer-rules package and extended-rules flag are unaffected by
  the downgrade.
- When an analyzer references a newer Roslyn than the host compiler, the compiler emits a **warning** (`CS9057` /
  `CS8032`) and then skips the assembly before registering any generator. The failure mode of a floor violation is
  therefore exactly this defect, which is why the floor is documented prominently rather than merely chosen.
- No change to the packed `build/` and `buildTransitive/` props: an `analyzers/` folder flows to transitive consumers on
  its own. No change for the `db` tool, which has no table definitions.

### The storage claim

- `ColumnAttribute` gains a `StoredAs` property typed as Npgsql's `NpgsqlDbType`. Npgsql is already a public dependency
  of the package, so this introduces no coupling consumers do not have. Using it rather than a library-owned enum makes
  claim and wire representation the same value on the Dapper surface, leaving one translation table instead of two.
- **"Unclaimed" needs no sentinel.** An attribute property cannot be `Nullable<TEnum>`, but the analyzer reads source:
  the absence of `StoredAs` from the attribute's named arguments is the absence of a claim, exactly how the existing
  nullability claim distinguishes "unstated" from a `false` default.
- The claim is applied **where the generated code binds the value**, never through a type registry. Two mechanisms, both
  at the binding site: a value conversion (an enum bound as its member-name string, a numeric type bound widened), and a
  parameter type (`jsonb` bound through the existing `ICustomQueryParameter` wrapper, which is what the library already
  shipped for this purpose and never used).
- The same claim is emitted into the Query surface registration. The public mapping builder's column method must
  therefore be able to carry a storage claim and a per-column conversion — it currently constructs the provider's
  per-member builder and discards it. Per-column data type and per-column conversion are both first-class in the
  provider, and a per-column conversion **is** applied during predicate translation, not only during materialisation, so
  `Where(x => x.State == TaskState.Open)` becomes a comparison against a parameter carrying `'Open'`.
- The provider's own enum mechanisms are deliberately **not** used. Both are keyed by enum type and process-wide — the
  same shape as the Dapper type-handler registry that produced this defect — and neither can express two columns of one
  enum stored differently.

### Defaults and the matrix

| Property type | Unclaimed behaviour | Claims that are exercised |
|---|---|---|
| `enum`, `enum?` | stored as the member name in text | `Text`, `Smallint`, `Integer`, `Bigint` |
| `string`, `string?` | bound as text, **no cast emitted** | `Text`, `Json`, `Jsonb` |
| `Dictionary<string, string>` | `jsonb`, as today | `Jsonb`, `Json` |
| `sbyte` | bound as `Smallint` automatically | `Smallint` |
| anything else | driver inference, unchanged | — |

- The unclaimed-`string` row is load-bearing: the driving application has roughly six `text` columns holding JSON that
  are cast at query time, and they must keep binding as text.
- Reading an enum from a `text` column needs nothing on the Dapper side — Dapper converts it natively and
  **case-insensitively** with no handler registered. The provider-side conversion matches that case-insensitivity
  deliberately, so a value differing in case from the member name behaves the same on both surfaces. Two enum members
  differing only by case will throw at parse time on either surface and are not diagnosed.
- `sbyte` is legal because Npgsql maps it to `int2` natively and by default; the documented failure happens only because
  Dapper infers a `DbType` Npgsql has no mapping for. Binding an explicit type is what the claim machinery does anyway.
- `ushort`, `uint` and `ulong` are refused as column types outright, regardless of any claim. This is verifiable
  non-support: Npgsql registers no integer or numeric mapping for any of them, by inference or explicitly — `uint`
  exists only for object-identifier types, `ulong` only for transaction-id and log-sequence types.

### Permitted versus supported

- Refusal is grounded in demonstrated failure, not in absence of a test. A storage claim outside the table above is
  **permitted**, and joins the refused set only when a test in this repository demonstrates it failing. The alternative
  — an allowlist of implemented combinations — was rejected because the list would be a hand-maintained mirror of the
  driver's internal mapping tables, which is the drift this release exists to remove.
- Where a claim has no representation on the Query surface (`Inet`, `Cidr`, the geometry types), the claim is honoured
  on the Dapper surface, the provider data type is simply not stated, and the build warns. That divergence is the one
  this release removes elsewhere and cannot remove there, so it is made visible rather than silent.
- The analyzer's mappable-type list shrinks but does not disappear. Enum, JSON and numeric knowledge moves into the
  per-column claim, where one declaration feeds both surfaces. The remaining entries still mirror the conversions the
  library registers process-wide for `Uri` and `Dictionary<string, string>`, by hand, because the analyzer cannot
  reference the library. That mirror is left as it is.

### Diagnostics

- `PGSQL0022` — **Error.** A storage claim demonstrated to fail for the property's type. Its table starts nearly empty
  by design; the diagnostic message names what is legal instead.
- `PGSQL0023` — **Error.** The property's type cannot be written by a generated repository. Covers `ushort`, `uint`,
  `ulong`. The existing unmappable-type diagnostic is only a warning and its advice — register a conversion — does not
  help, which is why this is a new rule rather than a reworded one.
- `PGSQL0024` — **Warning.** The storage claim has no Query surface representation; commands will use it and `Query()`
  will not.
- The property-shape rule drops its setter-accessibility requirement and keeps its requirement that a setter exist, so
  get-only and expression-bodied members stay refused and the computed-member guard survives. Every other part of that
  rule — non-static, public, non-indexer, public getter — is unchanged.
- All shipped diagnostics move from the unshipped to the shipped analyzer-release file under a `0.35` release. Twenty of
  them have sat unshipped because the analyzer had never actually shipped; this is the release that makes the record
  true.

### Generated output

- The generated data type mirrors `[Generated]` columns as `{ get; private set; }` and every other column as
  `{ get; set; }`. Dapper writes non-public and init-only setters, confirmed from its source and its own tests. The
  provider is unverified on this point and has a documented backing-field escape hatch if it cannot; a test settles it.
- `required` is not mirrored: the generated type has no constructor that could satisfy it.
- Nothing else about the generated shape changes. The table definition remains purely declarative and is never
  instantiated.

### Adjacent fixes taken in this pass

- The shipped enum type handler's null guard is dead code: converting `DBNull` yields an empty string, never null, so the
  guard cannot fire and the parse that follows throws. Whether Dapper shields a handler from `DBNull` before calling it
  is the part a test has to settle — if it does not, every nullable enum column read through a registered handler throws,
  and those are pervasive in the driving application. Either way the guard is wrong as written.
- The two data-source construction paths disagree about Npgsql's dynamic-JSON setting — the factory enables it, the
  direct construction path does not. They are aligned by enabling it in both, which is additive and cannot break a
  caller, whereas removing it could.
- The OData conformance fixture's integer-backed enum column takes an explicit `Integer` claim. This keeps its
  documented "explicit values are part of the fixture" note true, preserves the tests already pinned against it, and
  exercises the integer claim end to end.
- The filed idea about unwritable numeric types is retired: three refused, one fixed.

### Version and documentation

- Shared product version becomes `0.35.0`. MINOR under the project's pre-1.0 rule: the public surface gains members and
  one documented behaviour changes.
- The main README gains the storage claim, the table of exercised combinations, the minimum SDK, and a call-out of the
  enum default. The OData suite's README loses its "these four types cannot be written" note in favour of what is now
  true.
- The domain glossary gains **Storage claim** and states that a **Table definition** is never instantiated. The decision
  is recorded as an ADR covering per-column-over-per-type, the Npgsql-typed spelling, binding-site application,
  text-by-default, and blacklist-over-allowlist.

## Testing Decisions

A good test here asserts observable behaviour: what SQL text is emitted, which diagnostics are reported, what the
database contains after a write, what rows come back, and whether a package installs. It does not reach into how the
generator partitions its property sets or which builder method the registration used. Every claim in the matrix above is
a promise, so each row gets a test; every combination *not* in it is documented as untested, and the tests must not
imply otherwise.

**Existing seams, preferred over new ones:**

- **The generator harness** in the analyzer test project. It already drives the generator over a source string, returns
  the emitted source, and asserts the emitted source compiles against a stub surface. This is the seam for every
  diagnostic, for the setter relaxation, for the generated data type's setter shapes, and for the emitted SQL and
  parameter-binding text. Prior art: the existing generator suites for CRUD emission, composite keys and nullability,
  which assert on substrings of the emitted source. Follow that style — a snapshot of whole generated files would pin
  formatting the release deliberately changes.
- **The integration suite's `TestBase`** and its checked-in generated repositories. Transaction-per-test against a
  containerised PostgreSQL, rolled back on dispose. This is the seam for text and integer enum round-trips, a `jsonb`
  write and read, an `sbyte` write, a `[Generated]` private-setter column materialising through both surfaces, a
  nullable enum column reading back as null, and `Query()` translating an enum predicate. Assert the SQL shape through
  the internal query-diagnostics helper where the point is the translation rather than the rows, as the OData suite
  already does.
- **The OData conformance fixtures**, for the integer-claimed enum column. No new fixture: the change is a claim on an
  existing definition, and the existing pinned tests are the assertion that it behaves as before.

**The one new seam:**

- **A packaging test project.** Justified only because the artifact under test — the produced `.nupkg` — has no other
  entry point in the repository, which is exactly why this defect survived. It packs the library, installs it into a
  throwaway project scaffolded at test time, builds it, and runs it.
  - Isolation: pack under a run-unique prerelease version *and* redirect the package cache to a temporary directory, so
    no stale package can be served from the developer's cache and the cache is not polluted.
  - The throwaway project targets all three of the library's frameworks, so `lib/` resolution is proven for each under
    one SDK.
  - The runtime assertion is one table exercising the whole release at once: a `[Generated]` `{ get; private set; }`
    timestamp, a text-stored enum, a `string` `jsonb` column, and a `Query()` predicate on the enum that returns the
    row. It receives a connection string for a container the test starts, rather than starting its own.
  - Known gap, accepted: the **minimum SDK** cannot be tested without installing an older SDK, so it is documented
    rather than asserted. The build machine has only the .NET 10 SDK.
  - This is the slowest test in the repository and it needs the package to exist before it runs, so the publish
    pipeline's build-then-test-without-rebuilding shape has to account for the ordering.

An assumption that is load-bearing and unverified, and must become a test rather than a comment: that Dapper honours a
custom query parameter when it arrives as a value inside the parameter dictionary the generated code builds. If it does
not, the `jsonb` binding mechanism changes and the decision behind it is worth revisiting.

## Out of Scope

- **Arbitrary objects serialised to `jsonb`.** Only `string` and `Dictionary<string, string>` are exercised. Mapping a
  POCO needs serializer options, a naming policy, a read-side deserialisation story and an interaction with Npgsql's
  dynamic JSON — its own design. The driving application holds all its JSON as `string`.
- **Native PostgreSQL enum types.** No consumer has one, and reaching them means the driver's own type-mapper API rather
  than a storage claim.
- **Supporting `ushort`, `uint` and `ulong`.** Refused, not deferred-with-intent. The driver has no mapping for them at
  all, so support would mean a widening conversion on write and a narrowing one on read, on both surfaces, for types no
  consumer uses.
- **Verifying a storage claim, or any other claim, against a real table.** The claim joins column names, composite keys,
  generated columns and nullability as something the library takes the definition's word for. Checking a definition
  against a pulled schema is deliberately not attempted here, though this release increases what it would be worth.
- **Eliminating the last of the analyzer's hand-kept type mirror.** The `Uri` and `Dictionary<string, string>` entries
  stay hand-maintained; only the enum, JSON and numeric parts move into the claim.
- **Publishing the analyzer as its own package.** Decided against, not postponed.
- **Testing the minimum SDK.** Documented instead; see the accepted gap above.
- **Changing anything that already works.** Composite primary keys and their declaration-order contract, relations with
  composite foreign keys, eager loading, schema-qualified table names, the translated queryable surface, and the
  polymorphic-junction pattern over stored generated columns are all confirmed working and are not touched.
- **Enum member names diverging from their stored text.** The driving application maintains name-identical storage by
  data migration, and no mapping mechanism for divergence is introduced.
- **A general opt-out attribute for a member that is not a column.** Keeping the setter requirement preserves the
  computed-member guard without one.

## Further Notes

- **Why the blast radius is smaller than it looks.** Defect 1 establishes that the generated-repository surface has no
  package consumers, so every generator-side change here is non-breaking in practice. The one genuinely breaking surface
  is the enum default, because the mapping registry and its configuration hook are public and documented, so a
  hand-rolled query-surface consumer can be affected. It breaks loudly.
- **What the driving application looks like**, since several decisions are calibrated to it: 54 tables, 56 hand-written
  repositories, ~35 polymorphic-junction relations, 48 enums all stored as `text` with names identical to their C#
  members and spelled out in `CHECK` constraints, exactly four `jsonb` columns all held as `string`, roughly six `text`
  columns holding JSON, three `bytea` columns as `byte[]`, and no `sbyte`, `ushort`, `uint`, `ulong`, `char`, `Uri` or
  `Dictionary<string, string>` columns anywhere. It targets `net10.0` with no SDK pin and builds on the .NET 10 SDK, so
  the Roslyn floor is a reach question for the library rather than a constraint from this consumer.
- **What the published version corresponds to.** The behaviour on NuGet is the behaviour at `HEAD`: the three commits
  since the published version touch nothing under `src/`.
- **A consequence worth stating plainly.** The generated data type flattens `required`, `init` and `private set` to
  plain settable properties for every column that is not `[Generated]`. An application whose row types express
  caller-supplied-versus-database-populated through that pairing keeps only half of it. The `[Generated]` half is the
  half that matters, and it is preserved.
