# Tool Refactoring Plan

## Goals

1. Refactor `db pull`
2. Consolidate migrate command logic
3. Split `ToolConfiguration` responsibilities
4. Strengthen test seams around the tool

## 1. Refactor `db pull`

Goal: make `PullCommand` thin.

Target split:

- `src/mvdmio.Database.PgSQL.Tool/Commands/PullCommand.cs`
  - CLI options only
  - delegates to handler
- `src/mvdmio.Database.PgSQL.Tool/Pull/PullHandler.cs`
  - top-level orchestration
- `src/mvdmio.Database.PgSQL.Tool/Pull/SchemaExportService.cs`
  - connect to DB
  - call `SchemaExtractor`
  - return schema SQL + table/constraint metadata
- `src/mvdmio.Database.PgSQL.Tool/Pull/TableDefinitionWriter.cs`
  - resolve `Tables` path
  - call scaffolder
  - write generated files
- keep `src/mvdmio.Database.PgSQL.Tool/Scaffolding/TableDefinitionScaffolder.cs` for now, but make it purely `metadata -> code`

Suggested handler flow:

```csharp
config = configLoader.Load();
context = connectionResolver.Resolve(config, connectionOverride, environmentOverride);
schemaResult = schemaExportService.Export(context);
schemaFileWriter.Write(schemaResult.Script);
tableDefinitionWriter.Write(schemaResult.Tables, schemaResult.Constraints);
reporter.WriteSuccess(...);
```

Why first:

- biggest current hotspot
- recent feature work made it the clearest cohesion problem

## 2. Consolidate migrate command logic

Goal: remove duplication between `migrate latest` and `migrate to`.

Target split:

- `src/mvdmio.Database.PgSQL.Tool/Commands/MigrateLatestCommand.cs`
- `src/mvdmio.Database.PgSQL.Tool/Commands/MigrateToCommand.cs`
  - thin wiring only
- `src/mvdmio.Database.PgSQL.Tool/Migrations/MigrateHandler.cs`
  - shared orchestration
- `src/mvdmio.Database.PgSQL.Tool/Migrations/MigrateRequest.cs`
  - target mode: latest or identifier
- `src/mvdmio.Database.PgSQL.Tool/Migrations/MigrationProjectLoader.cs`
  - build/load assembly
- `src/mvdmio.Database.PgSQL.Tool/Migrations/MigrationExecutionService.cs`
  - run migration plan

Shared concerns to unify:

- config load
- connection resolution
- project build/load
- migration discovery
- console messages
- error handling

Only variation should be target selection.

## 3. Split `ToolConfiguration` responsibilities

Goal: `ToolConfiguration` becomes a plain config model.

Current `ToolConfiguration` does too much:

- YAML DTO
- file discovery
- loading
- path resolution
- connection/environment resolution

Split into:

- `src/mvdmio.Database.PgSQL.Tool/Configuration/ToolConfiguration.cs`
  - properties only
- `src/mvdmio.Database.PgSQL.Tool/Configuration/ToolConfigurationLoader.cs`
  - find config file
  - deserialize YAML
- `src/mvdmio.Database.PgSQL.Tool/Configuration/ToolPathResolver.cs`
  - project path
  - project directory
  - migrations directory
  - schemas directory
- `src/mvdmio.Database.PgSQL.Tool/Configuration/ConnectionStringResolver.cs`
  - resolve connection string
  - resolve environment name
  - list available environments

Nice side effect:

- commands/handlers stop depending on one `god object`

## 4. Strengthen test seams

Goal: make refactoring safe before more features.

Add tests around extracted services first, not command parsing first.

Priority tests:

- `NamespaceResolver`
  - root namespace present
  - fallback to project name
  - nested output directories
- `ProjectBuilder`
  - directory vs explicit `.csproj`
  - multiple `.csproj`
  - build failure reporting
- `TableDefinitionScaffolder`
  - duplicate names across schemas
  - composite PK case
  - more type mappings
  - quoted identifiers
- new `ConnectionStringResolver`
  - current `ToolConfiguration` tests can move here
- new `PullHandler` / `SchemaExportService`
  - through fake abstractions for file writing and reporting
- new `MigrateHandler`
  - latest vs target path

I would not start with full CLI invocation tests yet; service-level tests give more value faster.

## Suggested Sequence

### Phase 1

- split `ToolConfiguration`
- keep commands behavior identical
- move existing tests accordingly

### Phase 2

- extract `PullHandler`, `SchemaExportService`, `TableDefinitionWriter`
- keep `db pull` CLI unchanged

### Phase 3

- extract `MigrateHandler` and shared migrate services
- keep both migrate commands unchanged externally

### Phase 4

- clean up `TableDefinitionScaffolder`
- split into smaller internal helpers if still growing

## Acceptance Criteria

- command UX unchanged
- file outputs unchanged
- all existing tests pass
- new unit tests cover extracted services
- `Program.cs` and command files become mostly wiring
- no behavior regressions in `db pull`, `db migrate latest`, `db migrate to`

## Target Structure

```text
src/mvdmio.Database.PgSQL.Tool/
  Commands/
    PullCommand.cs
    MigrateLatestCommand.cs
    MigrateToCommand.cs
  Configuration/
    ToolConfiguration.cs
    ToolConfigurationLoader.cs
    ToolPathResolver.cs
    ConnectionStringResolver.cs
  Pull/
    PullHandler.cs
    SchemaExportService.cs
    TableDefinitionWriter.cs
  Migrations/
    MigrateHandler.cs
    MigrateRequest.cs
    MigrationProjectLoader.cs
    MigrationExecutionService.cs
  Scaffolding/
    NamespaceResolver.cs
    MigrationScaffolder.cs
    TableDefinitionScaffolder.cs
```

## Recommended Start

Start with `ToolConfiguration` split first, then `db pull`. That gives the best payoff with the least risk.
