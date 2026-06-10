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
