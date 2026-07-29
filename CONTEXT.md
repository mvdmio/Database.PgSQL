# mvdmio.Database.PgSQL

Glossary for the PostgreSQL data-access and migration library. Defines the domain language used when reasoning about migrations and their identity, about the table definitions code is generated from, and about the query surface generated over them.

## Language

**Migration**:
A single, ordered change to the database schema or data, implemented as an `IDbMigration`. Identified by its **Identifier** and grouped within a **Scope**.
_Avoid_: Script, change-set, upgrade.

**Identifier**:
A `YYYYMMDDHHmm` timestamp that orders a **Migration** within its **Scope**. Unique per scope, not globally.
_Avoid_: Version, sequence number.

**Scope**:
The stable logical timeline a **Migration** belongs to and is watermarked within. Defaults to the declaring assembly's simple name; overridable on `IDbMigration` to survive assembly renames or to deliberately split/share a timeline. Two scopes advance independently — a migration is run if its identifier is ahead of the watermark *for its own scope*, regardless of other scopes.
_Avoid_: Assembly name (it defaults to that but is not bound to it), namespace, module.

**Watermark**:
The highest executed **Identifier** within a single **Scope**. Migrations with an identifier above their scope's watermark are pending. Tracked per scope, not globally.
_Avoid_: High-water mark, version, checkpoint.

**Owned scope**:
A **Scope** an application declares as its own in the tool configuration (`scopes` in `.mvdmio-migrations.yml`). Schema export writes header watermark lines only for owned scopes, so a schema pulled from a shared database never names another application's timeline. Undeclared means all scopes (legacy behavior).
_Avoid_: Included scope, exported scope.

**Vouched scope**:
A **Scope** an assembly may establish a schema-first baseline for: the scopes of migrations discovered from that assembly, plus the assembly's simple name. Header lines for non-vouched scopes are ignored with a warning during bootstrap.
_Avoid_: Trusted scope, verified scope.

**Table definition**:
A class that declares one database table's shape — its name, its columns, and which of them form the primary key. The single source every generated type for that table derives from. Named with a `Table` suffix by convention. Purely declarative: it is never instantiated and nothing reads or writes it at run time, so what its members permit a caller to do is not a constraint the library has any stake in.
_Avoid_: Entity, model, POCO, mapping.

**Key order**:
The order of a **Table definition**'s primary-key properties, taken from their source declaration order. It fixes the parameter order of the generated primary-key lookup and the order a **Relation**'s foreign-key properties are matched in, so it is part of the generated API rather than a detail of the mapping.
_Avoid_: Key ordinal, column order, index order.

**Nullability claim**:
What a **Table definition** states about whether one of its columns can hold null. Where it states nothing the column is taken to be nullable, matching PostgreSQL's own column default; a primary-key member is never nullable, because the database will not permit it. Never verified against the real table, and load-bearing rather than descriptive — the **Query surface** narrows a predicate on the strength of it, so a column holding a null it was claimed not to returns fewer rows than it should rather than failing.
_Avoid_: Nullability, NOT NULL constraint (that is the database's, which this never creates and never checks), required, optional.

**Storage claim**:
What a **Table definition** states about how one of its columns is represented in PostgreSQL, spelled as the type the value is bound as. Where it states nothing the representation follows from the property's own type, except an enum, which is stored as the text of its member name. Never verified against the real table, and permissive rather than curated: a claim the library has no test for is still carried, so only a documented subset is known to round-trip, and one the **Query surface** cannot represent is honoured on the Dapper surface alone.
_Avoid_: Column type, DbType, DataType, cast, conversion.

**Entity name**:
A **Table definition**'s class name with the `Table` suffix removed. The stem every generated type name is built from, so it — not the table name — is what appears in consuming code.
_Avoid_: Table name, class name, type name.

**Relation**:
A declared correspondence between two **Table definitions**, resolved through the foreign-key columns one of the two holds and matched in order against the other's primary key. One-directional: each direction is declared on its own, and declaring one does not imply the other. A claim about columns that already exist — declaring a relation never creates a database foreign key and never verifies that one is there.
_Avoid_: Association (that is the LINQ provider's word), foreign key (that is the database constraint), relationship, join.

**Relation property**:
The member on a **Table definition** that declares a **Relation** — typed as the other Table definition, naming the foreign-key properties that resolve it, and carrying the cardinality in its own type. Not a column: it is skipped by column mapping and mirrored onto the generated data type, where each end appears as that table's generated data type.
_Avoid_: Navigation property (it implies lazy loading and change tracking, which this library does not have), reference, link.

**Query surface**:
The deferred, composable read path over a **Table definition**'s table, reached through a generated repository's `Query()` and backed by the `Linq` adapter. Read-only: it never mutates. It spans tables only along a declared **Relation** — filtering and ordering across one, and materializing the related rows when explicitly asked. Distinct from the Dapper surface, which every other generated method runs on and which never spans tables — the two derive from the same **Table definition** but keep separate conversion registries.
_Avoid_: LINQ provider (that is the dependency underneath), ORM, query builder.

**Query front-end**:
A consumer-side component that turns an external request into LINQ operators over the **Query surface** — an OData endpoint, for example. Always outside this library, which depends on none and knows of none.
_Avoid_: API layer, query API, OData layer, presentation layer.

**Translation boundary**:
The line between expressions the **Query surface** converts to SQL and those it refuses. Crossing it raises a query translation exception; the surface never silently falls back to evaluating in memory. Read-only by construction, and cross-table only along a declared **Relation**, but beyond that the translatable set cannot be enumerated from the type system — it is established by test, per **Query front-end**.
_Avoid_: Supported operators, provider limits, capability set.
