using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Migrations.Models;
using System.Text;

namespace mvdmio.Database.PgSQL.Connectors.Schema;

/// <summary>
///    Extracts the database schema from a PostgreSQL database by querying the system catalogs.
///    All methods exclude system schemas (pg_catalog, information_schema, pg_toast) and the
///    migration tracking schema (mvdmio).
/// </summary>
[PublicAPI]
public sealed class SchemaExtractor
{
   private const string MIGRATIONS_SCHEMA = "mvdmio";
   private const string MIGRATIONS_TABLE = "migrations";
   private const string MIGRATIONS_TABLE_FULLY_QUALIFIED = "\"mvdmio\".\"migrations\"";

   private readonly DatabaseConnection _db;
   private readonly SchemaCatalogReader _catalog;
   private readonly IReadOnlyCollection<string>? _includedSchemas;

   /// <summary>
   ///    Initializes a new instance of the <see cref="SchemaExtractor"/> class.
   /// </summary>
   /// <param name="db">The database connection to use for schema extraction.</param>
   public SchemaExtractor(DatabaseConnection db)
      : this(db, null)
   {
   }

   /// <summary>
   ///    Initializes a new instance of the <see cref="SchemaExtractor"/> class.
   /// </summary>
   /// <param name="db">The database connection to use for schema extraction.</param>
   /// <param name="includedSchemas">Optional set of schemas to export. When null or empty, all user schemas are exported.</param>
   public SchemaExtractor(DatabaseConnection db, IReadOnlyCollection<string>? includedSchemas)
   {
      _db = db;
      _includedSchemas = includedSchemas is { Count: > 0 } ? includedSchemas : null;
      _catalog = new SchemaCatalogReader(db, _includedSchemas);
   }

   /// <summary>
   ///    Gets the schemas explicitly included for export, or null when exporting all user schemas.
   /// </summary>
   [PublicAPI]
   public IReadOnlyCollection<string>? IncludedSchemas => _includedSchemas;

   /// <summary>
   ///    Gets the current migration version (the most recently executed migration).
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The latest executed migration, or null if no migrations have been executed or the migration table does not exist.</returns>
   public async Task<ExecutedMigrationModel?> GetCurrentMigrationVersionAsync(CancellationToken cancellationToken = default)
   {
      var schemaExists = await _db.Management.SchemaExistsAsync(MIGRATIONS_SCHEMA);
      if (!schemaExists)
         return null;

      var tableExists = await _db.Management.TableExistsAsync(MIGRATIONS_SCHEMA, MIGRATIONS_TABLE);
      if (!tableExists)
         return null;

      return await _db.Dapper.QuerySingleOrDefaultAsync<ExecutedMigrationModel>(
         $"""
         SELECT
            identifier AS identifier,
            name AS name,
            executed_at AS executedAtUtc
         FROM {MIGRATIONS_TABLE_FULLY_QUALIFIED}
         ORDER BY identifier DESC
         LIMIT 1
         """,
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Retrieves all non-default extensions installed in the database.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The installed extensions.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.ExtensionInfo>> GetExtensionsAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetExtensionsAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all user-created schemas, excluding system schemas, the public schema, and the mvdmio migration schema.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The schema names.</returns>
   [PublicAPI]
   public Task<IEnumerable<string>> GetUserSchemasAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetUserSchemasAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all exportable schemas, excluding only system schemas and the mvdmio migration schema.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The schema names, including public.</returns>
   [PublicAPI]
   public Task<IEnumerable<string>> GetExportableSchemasAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetExportableSchemasAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all user-defined enum types.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The enum types with their labels.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.EnumTypeInfo>> GetEnumTypesAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetEnumTypesAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all user-defined composite types.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The composite types with their attributes.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.CompositeTypeInfo>> GetCompositeTypesAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetCompositeTypesAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all user-defined domain types.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The domain types.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.DomainTypeInfo>> GetDomainTypesAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetDomainTypesAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all user-defined sequences.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The sequences.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.SequenceInfo>> GetSequencesAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetSequencesAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all user tables with their columns.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The tables with column information.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.TableInfo>> GetTablesAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetTablesAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all constraints for user tables.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The constraints.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.ConstraintInfo>> GetConstraintsAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetConstraintsAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all indexes that are not created by constraints.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The indexes.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.IndexInfo>> GetIndexesAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetIndexesAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all user-defined functions and procedures.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The functions with their full definitions.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.FunctionInfo>> GetFunctionsAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetFunctionsAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all user-defined triggers.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The triggers.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.TriggerInfo>> GetTriggersAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetTriggersAsync(cancellationToken);
   }

   /// <summary>
   ///    Retrieves all user-defined views.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The views.</returns>
   [PublicAPI]
   public Task<IEnumerable<Connectors.Schema.Models.ViewInfo>> GetViewsAsync(CancellationToken cancellationToken = default)
   {
      return _catalog.GetViewsAsync(cancellationToken);
   }

   /// <summary>
   ///    Generates a complete, idempotent SQL script that recreates the database schema.
   ///    The script includes extensions, schemas, types, sequences, tables, constraints,
   ///    indexes, functions, triggers, and views.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>A SQL script string that can be executed to recreate the database schema.</returns>
   public async Task<string> GenerateSchemaScriptAsync(CancellationToken cancellationToken = default)
   {
      var sb = new StringBuilder();

      sb.AppendLine("--");
      sb.AppendLine("-- PostgreSQL database schema");
      sb.AppendLine($"-- Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

      var currentMigration = await GetCurrentMigrationVersionAsync(cancellationToken);
      if (currentMigration is not null)
         sb.AppendLine($"-- Migration version: {currentMigration.Value.Identifier} ({currentMigration.Value.Name})");
      else
         sb.AppendLine("-- Migration version: (none)");

      sb.AppendLine("--");
      sb.AppendLine();

      var tables = (await GetTablesAsync(cancellationToken)).ToArray();
      var constraints = (await GetConstraintsAsync(cancellationToken)).ToArray();

      await AppendExtensionsAsync(sb, cancellationToken);
      await AppendSchemasAsync(sb, cancellationToken);
      await AppendEnumTypesAsync(sb, cancellationToken);
      await AppendCompositeTypesAsync(sb, cancellationToken);
      await AppendDomainTypesAsync(sb, cancellationToken);
      await AppendSequencesAsync(sb, cancellationToken);
      SchemaScriptTableRenderer.AppendTables(sb, tables, constraints);
      await AppendSequenceOwnershipAsync(sb, cancellationToken);
      SchemaScriptTableRenderer.AppendConstraints(sb, constraints, EscapeSqlString);
      await AppendIndexesAsync(sb, cancellationToken);
      await AppendFunctionsAsync(sb, cancellationToken);
      await AppendTriggersAsync(sb, cancellationToken);
      await AppendViewsAsync(sb, cancellationToken);

      return sb.ToString();
   }

   private async Task AppendExtensionsAsync(StringBuilder sb, CancellationToken ct)
   {
      var extensions = (await GetExtensionsAsync(ct)).ToArray();

      if (extensions.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Extensions");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var ext in extensions)
      {
         sb.AppendLine($"CREATE EXTENSION IF NOT EXISTS \"{ext.Name}\" SCHEMA \"{ext.Schema}\";");
      }

      sb.AppendLine();
   }

   private async Task AppendSchemasAsync(StringBuilder sb, CancellationToken ct)
   {
      var schemas = (await GetUserSchemasAsync(ct)).ToArray();

      if (schemas.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Schemas");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var schema in schemas)
      {
         sb.AppendLine($"CREATE SCHEMA IF NOT EXISTS \"{schema}\";");
      }

      sb.AppendLine();
   }

   private async Task AppendEnumTypesAsync(StringBuilder sb, CancellationToken ct)
   {
      var enumTypes = (await GetEnumTypesAsync(ct)).ToArray();

      if (enumTypes.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Enum types");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var enumType in enumTypes)
      {
         var labels = string.Join(", ", enumType.Labels.Select(l => $"'{EscapeSqlString(l)}'"));

         sb.AppendLine("DO $$ BEGIN");
         sb.AppendLine($"   IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON t.typnamespace = n.oid WHERE t.typname = '{EscapeSqlString(enumType.Name)}' AND n.nspname = '{EscapeSqlString(enumType.Schema)}') THEN");
         sb.AppendLine($"      CREATE TYPE \"{enumType.Schema}\".\"{enumType.Name}\" AS ENUM ({labels});");
         sb.AppendLine("   END IF;");
         sb.AppendLine("END $$;");
         sb.AppendLine();
      }
   }

   private async Task AppendCompositeTypesAsync(StringBuilder sb, CancellationToken ct)
   {
      var compositeTypes = (await GetCompositeTypesAsync(ct)).ToArray();

      if (compositeTypes.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Composite types");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var compositeType in compositeTypes)
      {
         var attributes = string.Join(",\n   ", compositeType.Attributes.Select(a => $"\"{a.Name}\" {a.DataType}"));

         sb.AppendLine("DO $$ BEGIN");
         sb.AppendLine($"   IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON t.typnamespace = n.oid WHERE t.typname = '{EscapeSqlString(compositeType.Name)}' AND n.nspname = '{EscapeSqlString(compositeType.Schema)}') THEN");
         sb.AppendLine($"      CREATE TYPE \"{compositeType.Schema}\".\"{compositeType.Name}\" AS (");
         sb.AppendLine($"         {attributes}");
         sb.AppendLine("      );");
         sb.AppendLine("   END IF;");
         sb.AppendLine("END $$;");
         sb.AppendLine();
      }
   }

   private async Task AppendDomainTypesAsync(StringBuilder sb, CancellationToken ct)
   {
      var domainTypes = (await GetDomainTypesAsync(ct)).ToArray();

      if (domainTypes.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Domain types");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var domain in domainTypes)
      {
         sb.AppendLine("DO $$ BEGIN");
         sb.AppendLine($"   IF NOT EXISTS (SELECT 1 FROM pg_type t JOIN pg_namespace n ON t.typnamespace = n.oid WHERE t.typname = '{EscapeSqlString(domain.Name)}' AND n.nspname = '{EscapeSqlString(domain.Schema)}') THEN");

         var domainDef = $"CREATE DOMAIN \"{domain.Schema}\".\"{domain.Name}\" AS {domain.BaseType}";

         if (domain.DefaultValue is not null)
            domainDef += $" DEFAULT {domain.DefaultValue}";

         if (domain.IsNotNull)
            domainDef += " NOT NULL";

         foreach (var check in domain.CheckConstraints)
         {
            domainDef += $" {check}";
         }

         sb.AppendLine($"      {domainDef};");
         sb.AppendLine("   END IF;");
         sb.AppendLine("END $$;");
         sb.AppendLine();
      }
   }

   private async Task AppendSequencesAsync(StringBuilder sb, CancellationToken ct)
   {
      var sequences = (await GetSequencesAsync(ct)).ToArray();

      if (sequences.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Sequences");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var seq in sequences)
      {
         sb.Append($"CREATE SEQUENCE IF NOT EXISTS \"{seq.Schema}\".\"{seq.Name}\"");
         sb.Append($" AS {seq.DataType}");
         sb.Append($" INCREMENT BY {seq.IncrementBy}");
         sb.Append($" MINVALUE {seq.MinValue}");
         sb.Append($" MAXVALUE {seq.MaxValue}");
         sb.Append($" START WITH {seq.StartValue}");
         sb.Append($" CACHE {seq.CacheSize}");
         sb.Append(seq.IsCyclic ? " CYCLE" : " NO CYCLE");
         sb.AppendLine(";");
      }

      sb.AppendLine();
   }

   private async Task AppendSequenceOwnershipAsync(StringBuilder sb, CancellationToken ct)
   {
      var ownedSequences = (await GetSequencesAsync(ct))
         .Where(s => s.OwnedByTable is not null && s.OwnedByColumn is not null)
         .ToArray();

      if (ownedSequences.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Sequence ownership");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var seq in ownedSequences)
      {
         sb.AppendLine($"ALTER SEQUENCE \"{seq.Schema}\".\"{seq.Name}\" OWNED BY \"{seq.Schema}\".\"{seq.OwnedByTable}\".\"{seq.OwnedByColumn}\";");
      }

      sb.AppendLine();
   }

   private async Task AppendIndexesAsync(StringBuilder sb, CancellationToken ct)
   {
      var indexes = (await GetIndexesAsync(ct)).ToArray();

      if (indexes.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Indexes");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var index in indexes)
      {
         // pg_get_indexdef returns "CREATE INDEX name ON ..." or "CREATE UNIQUE INDEX name ON ..."
         // We need to inject "IF NOT EXISTS" after "CREATE INDEX" or "CREATE UNIQUE INDEX"
         var def = index.Definition;
         def = InjectIfNotExistsIntoIndexDef(def);

         sb.AppendLine($"{def};");
      }

      sb.AppendLine();
   }

   private async Task AppendFunctionsAsync(StringBuilder sb, CancellationToken ct)
   {
      var functions = (await GetFunctionsAsync(ct)).ToArray();

      if (functions.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Functions");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var func in functions)
      {
         // pg_get_functiondef returns "CREATE OR REPLACE FUNCTION ..." already
         var def = func.Definition;

         // Ensure it uses CREATE OR REPLACE
         if (!def.Contains("OR REPLACE", StringComparison.OrdinalIgnoreCase))
         {
            def = def.Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION", StringComparison.OrdinalIgnoreCase);
            def = def.Replace("CREATE PROCEDURE", "CREATE OR REPLACE PROCEDURE", StringComparison.OrdinalIgnoreCase);
         }

         sb.AppendLine(def);

         // pg_get_functiondef may or may not end with a semicolon
         if (!def.TrimEnd().EndsWith(';'))
            sb.AppendLine(";");

         sb.AppendLine();
      }
   }

   private async Task AppendTriggersAsync(StringBuilder sb, CancellationToken ct)
   {
      var triggers = (await GetTriggersAsync(ct)).ToArray();

      if (triggers.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Triggers");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var trigger in triggers)
      {
         // pg_get_triggerdef returns "CREATE TRIGGER ..."
         // Convert to "CREATE OR REPLACE TRIGGER ..." for idempotency (PG 14+)
         var def = trigger.Definition;

         if (!def.Contains("OR REPLACE", StringComparison.OrdinalIgnoreCase))
         {
            def = def.Replace("CREATE TRIGGER", "CREATE OR REPLACE TRIGGER", StringComparison.OrdinalIgnoreCase);
            def = def.Replace("CREATE CONSTRAINT TRIGGER", "CREATE OR REPLACE CONSTRAINT TRIGGER", StringComparison.OrdinalIgnoreCase);
         }

         sb.AppendLine($"{def};");
      }

      sb.AppendLine();
   }

   private async Task AppendViewsAsync(StringBuilder sb, CancellationToken ct)
   {
      var views = (await GetViewsAsync(ct)).ToArray();

      if (views.Length == 0)
         return;

      sb.AppendLine("-- ============================================================================");
      sb.AppendLine("-- Views");
      sb.AppendLine("-- ============================================================================");
      sb.AppendLine();

      foreach (var view in views)
      {
         var defTrimmed = view.Definition.TrimEnd();

         if (defTrimmed.EndsWith(';'))
            defTrimmed = defTrimmed[..^1];

         sb.AppendLine($"CREATE OR REPLACE VIEW \"{view.Schema}\".\"{view.Name}\" AS");
         sb.AppendLine($"{defTrimmed};");
         sb.AppendLine();
      }
   }

   private static string InjectIfNotExistsIntoIndexDef(string indexDef)
   {
      // Handle "CREATE UNIQUE INDEX name" and "CREATE INDEX name"
      const string createUniqueIndex = "CREATE UNIQUE INDEX ";
      const string createIndex = "CREATE INDEX ";

      if (indexDef.StartsWith(createUniqueIndex, StringComparison.OrdinalIgnoreCase))
         return $"CREATE UNIQUE INDEX IF NOT EXISTS {indexDef[createUniqueIndex.Length..]}";

      if (indexDef.StartsWith(createIndex, StringComparison.OrdinalIgnoreCase))
         return $"CREATE INDEX IF NOT EXISTS {indexDef[createIndex.Length..]}";

      return indexDef;
   }

   internal static string EscapeSqlString(string? value)
   {
      return value?.Replace("'", "''") ?? string.Empty;
   }
}
