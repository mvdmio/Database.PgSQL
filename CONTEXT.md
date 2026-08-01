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
The order of a **Table definition**'s primary-key properties, taken from their source declaration order. It fixes the parameter order of the generated primary-key lookup, so it is part of the generated API rather than a detail of the mapping. It says nothing about how a **Relation** matches columns; a **Relation key** states each pair itself.
_Avoid_: Key ordinal, column order, index order.

**Nullability claim**:
What a **Table definition** states about whether one of its columns can hold null. Where it states nothing the column is taken to be nullable, matching PostgreSQL's own column default; a primary-key member is never nullable, because the database will not permit it. Never verified against the real table, and load-bearing rather than descriptive — the **Query surface** narrows a predicate on the strength of it, so a column holding a null it was claimed not to returns fewer rows than it should rather than failing.
_Avoid_: Nullability, NOT NULL constraint (that is the database's, which this never creates and never checks), required, optional.

**Storage claim**:
What a **Table definition** states about how one of its columns is represented in PostgreSQL, spelled as the type the value is bound as. Where it states nothing the representation follows from the property's own type, except an enum, which is stored as the text of its member name. Never verified against the real table, and permissive rather than curated: a claim the library has no test for is still carried, so only a documented subset is known to round-trip, and one the **Query surface** cannot represent is honoured on the Dapper surface alone.
_Avoid_: Column type, DbType, DataType, cast, conversion.

**Tenancy column**:
A column a **Table definition** names as one that every generated member must constrain, so that no generated member can read or change a row belonging to another tenant. More than one column may be named, and each is constrained. What it changes is the generated API rather than any statement's meaning: a member that does not already constrain the column takes its value as a parameter, and the generated command types carry it as a required property, so leaving it out is a build error instead of a review responsibility. It reaches no further than generated code — the **Query surface** is still reachable directly, and hand-written Dapper is untouched. Never verified in any sense: nothing checks the column against the real table, and nothing checks the value against anything, because the library holds no tenant of its own and so cannot tell a right value from a wrong one. It makes the value impossible to omit and nothing more.
_Avoid_: Tenant, tenant id, partition key, discriminator, row-level security (that is PostgreSQL's, which this library never configures).

**Entity name**:
A **Table definition**'s class name with the `Table` suffix removed. The stem every generated type name is built from, so it — not the table name — is what appears in consuming code.
_Avoid_: Table name, class name, type name.

**Relation**:
A declared correspondence between two **Table definitions**, resolved through the column pairs its **Relation definition** states as equal and narrowed by that definition's **Relation condition** where it has one. One-directional: each direction is declared on its own, and declaring one does not imply the other. A claim about columns that already exist — declaring a relation never creates a database foreign key and never verifies that one is there.
_Avoid_: Association (that is the LINQ provider's word), foreign key (that is the database constraint), relationship, join.

**Relation definition**:
The class that declares one **Relation**, deriving from `RelationDefinition<TDeclaring, TTarget>`. Its two type arguments name the two **Table definitions**, and its members state the **Relation keys** and the **Relation condition**. Purely declarative in the same sense a Table definition is: never instantiated and never executed, because the generator reads what it says from source rather than running it.
_Avoid_: Configuration, mapping, builder, entity type configuration (that is Entity Framework's, and this library configures nothing at run time).

**Relation key**:
One pair of columns a **Relation definition** states as equal, one on each of the two **Table definitions**. A relation states one pair per column it joins on, and their order carries no meaning. A **Relation** to one row must pair against columns the target claims are unique — its primary key, or a column marked unique — so that it reaches one row rather than an arbitrary one of several.
_Avoid_: Foreign key (that is the database constraint), join key, key column, key pair.

**Relation condition**:
An extra condition a **Relation** carries beyond its **Relation keys** — any expression over the two rows, stated on the **Relation definition**. It is what lets two relations that pair the same columns reach different rows: one column names a kind, and each relation's condition fixes the value it reaches. It narrows filtering and materializing alike, because it belongs to the correspondence rather than to any one query.
_Avoid_: Discriminator, filter (that is what a caller writes over the **Query surface**), scope, predicate.

**Relation property**:
The member on a **Table definition** that carries a **Relation** — typed as that relation's **Relation definition**, or as a collection of one, which is how the cardinality is stated. Not a column: it is skipped by column mapping and mirrored onto the generated data type, where it appears as the target's generated data type rather than as the relation definition.
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
