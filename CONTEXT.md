# mvdmio.Database.PgSQL

Glossary for the PostgreSQL data-access and migration library. Defines the domain language used when reasoning about migrations, their identity, and how they are tracked.

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
A class that declares one database table's shape — its name, its columns, and which column is the primary key. The single source every generated type for that table derives from. Named with a `Table` suffix by convention.
_Avoid_: Entity, model, POCO, mapping.

**Entity name**:
A **Table definition**'s class name with the `Table` suffix removed. The stem every generated type name is built from, so it — not the table name — is what appears in consuming code.
_Avoid_: Table name, class name, type name.

**Query surface**:
The deferred, composable read path over a **Table definition**'s table, reached through a generated repository's `Query()` and backed by the `Linq` adapter. Read-only and single-table: it never mutates and never spans tables. Distinct from the Dapper surface, which every other generated method runs on — the two derive from the same **Table definition** but keep separate conversion registries.
_Avoid_: LINQ provider (that is the dependency underneath), ORM, query builder.
