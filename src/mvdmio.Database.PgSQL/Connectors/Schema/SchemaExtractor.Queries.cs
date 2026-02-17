using JetBrains.Annotations;
using mvdmio.Database.PgSQL.Connectors.Schema.Models;

namespace mvdmio.Database.PgSQL.Connectors.Schema;

/// <summary>
///    Catalog query methods for <see cref="SchemaExtractor"/>.
///    Each method queries PostgreSQL system catalogs to retrieve schema information.
/// </summary>
public sealed partial class SchemaExtractor
{
   /// <summary>
   ///    Retrieves all non-default extensions installed in the database.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The installed extensions.</returns>
   [PublicAPI]
   public async Task<IEnumerable<ExtensionInfo>> GetExtensionsAsync(CancellationToken cancellationToken = default)
   {
      return await _db.Dapper.QueryAsync<ExtensionInfo>(
         """
         SELECT
            e.extname       AS name,
            n.nspname       AS schema,
            e.extversion    AS version
         FROM pg_extension e
         JOIN pg_namespace n ON e.extnamespace = n.oid
         WHERE e.extname <> 'plpgsql'
         ORDER BY e.extname
         """,
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Retrieves all user-created schemas, excluding system schemas, the public schema, and the mvdmio migration schema.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The schema names.</returns>
   [PublicAPI]
   public async Task<IEnumerable<string>> GetUserSchemasAsync(CancellationToken cancellationToken = default)
   {
      return await _db.Dapper.QueryAsync<string>(
         $"""
         SELECT n.nspname
         FROM pg_namespace n
         WHERE {SCHEMA_FILTER}
           AND n.nspname <> 'public'
         ORDER BY n.nspname
         """,
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Retrieves all user-defined enum types.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The enum types with their labels.</returns>
   [PublicAPI]
   public async Task<IEnumerable<EnumTypeInfo>> GetEnumTypesAsync(CancellationToken cancellationToken = default)
   {
      var rows = await _db.Dapper.QueryAsync<EnumTypeRow>(
         $"""
         SELECT
            n.nspname       AS schema,
            t.typname       AS name,
            e.enumlabel     AS label,
            e.enumsortorder AS sort_order
         FROM pg_type t
         JOIN pg_namespace n ON t.typnamespace = n.oid
         JOIN pg_enum e ON e.enumtypid = t.oid
         WHERE t.typtype = 'e'
           AND {SCHEMA_FILTER}
         ORDER BY n.nspname, t.typname, e.enumsortorder
         """,
         ct: cancellationToken
      );

      return rows
         .GroupBy(r => (r.Schema, r.Name))
         .Select(g => new EnumTypeInfo { Schema = g.Key.Schema, Name = g.Key.Name, Labels = g.Select(r => r.Label).ToArray() })
         .ToArray();
   }

   /// <summary>
   ///    Retrieves all user-defined composite types (excluding table row types and other internal types).
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The composite types with their attributes.</returns>
   [PublicAPI]
   public async Task<IEnumerable<CompositeTypeInfo>> GetCompositeTypesAsync(CancellationToken cancellationToken = default)
   {
      var rows = await _db.Dapper.QueryAsync<CompositeTypeRow>(
         $"""
         SELECT
            n.nspname                                    AS schema,
            t.typname                                    AS type_name,
            a.attname                                    AS attribute_name,
            pg_catalog.format_type(a.atttypid, a.atttypmod) AS data_type
         FROM pg_type t
         JOIN pg_namespace n ON t.typnamespace = n.oid
         JOIN pg_class c ON c.oid = t.typrelid
         JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
         WHERE t.typtype = 'c'
           AND c.relkind = 'c'
           AND {SCHEMA_FILTER}
         ORDER BY n.nspname, t.typname, a.attnum
         """,
         ct: cancellationToken
      );

      return rows
         .GroupBy(r => (r.Schema, r.TypeName))
         .Select(g => new CompositeTypeInfo
         {
            Schema = g.Key.Schema,
            Name = g.Key.TypeName,
            Attributes = g.Select(r => new CompositeTypeAttributeInfo { Name = r.AttributeName, DataType = r.DataType }).ToArray()
         })
         .ToArray();
   }

   /// <summary>
   ///    Retrieves all user-defined domain types.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The domain types.</returns>
   [PublicAPI]
   public async Task<IEnumerable<DomainTypeInfo>> GetDomainTypesAsync(CancellationToken cancellationToken = default)
   {
      var rows = await _db.Dapper.QueryAsync<DomainTypeRow>(
         $"""
         SELECT
            n.nspname                                         AS schema,
            t.typname                                         AS name,
            pg_catalog.format_type(t.typbasetype, t.typtypmod) AS base_type,
            t.typdefault                                       AS default_value,
            t.typnotnull                                       AS is_not_null
         FROM pg_type t
         JOIN pg_namespace n ON t.typnamespace = n.oid
         WHERE t.typtype = 'd'
           AND {SCHEMA_FILTER}
         ORDER BY n.nspname, t.typname
         """,
         ct: cancellationToken
      );

      var result = new List<DomainTypeInfo>();

      foreach (var row in rows)
      {
         var checks = await _db.Dapper.QueryAsync<string>(
            """
            SELECT pg_catalog.pg_get_constraintdef(c.oid)
            FROM pg_constraint c
            WHERE c.contypid = (
               SELECT t.oid FROM pg_type t
               JOIN pg_namespace n ON t.typnamespace = n.oid
               WHERE t.typname = :typeName AND n.nspname = :schemaName
            )
            ORDER BY c.conname
            """,
            new Dictionary<string, object?> { ["typeName"] = row.Name, ["schemaName"] = row.Schema },
            ct: cancellationToken
         );

         result.Add(new DomainTypeInfo
         {
            Schema = row.Schema,
            Name = row.Name,
            BaseType = row.BaseType,
            DefaultValue = row.DefaultValue,
            IsNotNull = row.IsNotNull,
            CheckConstraints = checks.ToArray()
         });
      }

      return result;
   }

   /// <summary>
   ///    Retrieves all user-defined sequences.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The sequences.</returns>
   [PublicAPI]
   public async Task<IEnumerable<SequenceInfo>> GetSequencesAsync(CancellationToken cancellationToken = default)
   {
      return await _db.Dapper.QueryAsync<SequenceInfo>(
         $"""
         SELECT
            n.nspname             AS schema,
            c.relname             AS name,
            pg_catalog.format_type(s.seqtypid, NULL) AS data_type,
            s.seqstart            AS start_value,
            s.seqincrement        AS increment_by,
            s.seqmin              AS min_value,
            s.seqmax              AS max_value,
            s.seqcache            AS cache_size,
            s.seqcycle            AS is_cyclic,
            dep_c.relname         AS owned_by_table,
            dep_a.attname         AS owned_by_column
         FROM pg_sequence s
         JOIN pg_class c ON c.oid = s.seqrelid
         JOIN pg_namespace n ON n.oid = c.relnamespace
         LEFT JOIN pg_depend d ON d.objid = c.oid AND d.deptype = 'a' AND d.classid = 'pg_class'::regclass
         LEFT JOIN pg_class dep_c ON dep_c.oid = d.refobjid AND dep_c.relkind IN ('r', 'p')
         LEFT JOIN pg_attribute dep_a ON dep_a.attrelid = d.refobjid AND dep_a.attnum = d.refobjsubid AND NOT dep_a.attisdropped
         WHERE {SCHEMA_FILTER}
         ORDER BY n.nspname, c.relname
         """,
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Retrieves all user tables with their columns.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The tables with column information.</returns>
   [PublicAPI]
   public async Task<IEnumerable<TableInfo>> GetTablesAsync(CancellationToken cancellationToken = default)
   {
      var rows = await _db.Dapper.QueryAsync<TableColumnRow>(
         $"""
         SELECT
            n.nspname                                                AS schema,
            c.relname                                                AS table_name,
            a.attname                                                AS column_name,
            pg_catalog.format_type(a.atttypid, a.atttypmod)          AS data_type,
            NOT a.attnotnull                                         AS is_nullable,
            CASE WHEN a.attidentity = '' THEN pg_get_expr(d.adbin, d.adrelid) ELSE NULL END AS default_value,
            a.attidentity <> ''                                      AS is_identity,
            CASE a.attidentity WHEN 'a' THEN 'ALWAYS' WHEN 'd' THEN 'BY DEFAULT' ELSE NULL END AS identity_generation,
            a.attnum                                                 AS ordinal
         FROM pg_class c
         JOIN pg_namespace n ON n.oid = c.relnamespace
         JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
         LEFT JOIN pg_attrdef d ON d.adrelid = c.oid AND d.adnum = a.attnum
         WHERE c.relkind IN ('r', 'p')
           AND {SCHEMA_FILTER}
         ORDER BY n.nspname, c.relname, a.attnum
         """,
         ct: cancellationToken
      );

      return rows
         .GroupBy(r => (r.Schema, r.TableName))
         .Select(g => new TableInfo
         {
            Schema = g.Key.Schema,
            Name = g.Key.TableName,
            Columns = g.OrderBy(r => r.Ordinal)
               .Select(r => new ColumnInfo
               {
                  Name = r.ColumnName,
                  DataType = r.DataType,
                  IsNullable = r.IsNullable,
                  DefaultValue = r.DefaultValue,
                  IsIdentity = r.IsIdentity,
                  IdentityGeneration = r.IdentityGeneration
               })
               .ToArray()
         })
         .ToArray();
   }

   /// <summary>
   ///    Retrieves all constraints (primary key, foreign key, unique, check, exclusion) for user tables.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The constraints.</returns>
   [PublicAPI]
   public async Task<IEnumerable<ConstraintInfo>> GetConstraintsAsync(CancellationToken cancellationToken = default)
   {
      return await _db.Dapper.QueryAsync<ConstraintInfo>(
         $"""
         SELECT
            n.nspname                                     AS schema,
            c.relname                                     AS table_name,
            con.conname                                   AS constraint_name,
            con.contype::text                              AS constraint_type,
            pg_catalog.pg_get_constraintdef(con.oid, true) AS definition
         FROM pg_constraint con
         JOIN pg_class c ON c.oid = con.conrelid
         JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE c.relkind IN ('r', 'p')
           AND {SCHEMA_FILTER}
         ORDER BY
            CASE con.contype WHEN 'p' THEN 0 WHEN 'u' THEN 1 WHEN 'c' THEN 2 WHEN 'f' THEN 3 WHEN 'x' THEN 4 ELSE 5 END,
            n.nspname, c.relname, con.conname
         """,
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Retrieves all indexes that are not created by constraints (i.e., not primary key or unique constraint indexes).
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The indexes.</returns>
   [PublicAPI]
   public async Task<IEnumerable<IndexInfo>> GetIndexesAsync(CancellationToken cancellationToken = default)
   {
      return await _db.Dapper.QueryAsync<IndexInfo>(
         $"""
         SELECT
            n.nspname                                     AS schema,
            t.relname                                     AS table_name,
            i.relname                                     AS index_name,
            pg_catalog.pg_get_indexdef(ix.indexrelid)      AS definition
         FROM pg_index ix
         JOIN pg_class i ON i.oid = ix.indexrelid
         JOIN pg_class t ON t.oid = ix.indrelid
         JOIN pg_namespace n ON n.oid = t.relnamespace
         WHERE t.relkind IN ('r', 'p')
           AND NOT ix.indisprimary
           AND NOT ix.indisunique
           AND {SCHEMA_FILTER}
           AND NOT EXISTS (
              SELECT 1 FROM pg_constraint con
              WHERE con.conindid = ix.indexrelid
           )
         ORDER BY n.nspname, t.relname, i.relname
         """,
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Retrieves all user-defined functions and procedures.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The functions with their full definitions.</returns>
   [PublicAPI]
   public async Task<IEnumerable<FunctionInfo>> GetFunctionsAsync(CancellationToken cancellationToken = default)
   {
      return await _db.Dapper.QueryAsync<FunctionInfo>(
         $"""
         SELECT
            n.nspname                                         AS schema,
            p.proname                                         AS name,
            pg_catalog.pg_get_function_identity_arguments(p.oid) AS identity_arguments,
            pg_catalog.pg_get_functiondef(p.oid)               AS definition
         FROM pg_proc p
         JOIN pg_namespace n ON n.oid = p.pronamespace
         WHERE {SCHEMA_FILTER}
           AND p.prokind IN ('f', 'p')
           AND NOT EXISTS (
              SELECT 1 FROM pg_depend d
              WHERE d.objid = p.oid AND d.deptype = 'e'
           )
         ORDER BY n.nspname, p.proname
         """,
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Retrieves all user-defined triggers (excluding internal constraint triggers).
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The triggers.</returns>
   [PublicAPI]
   public async Task<IEnumerable<TriggerInfo>> GetTriggersAsync(CancellationToken cancellationToken = default)
   {
      return await _db.Dapper.QueryAsync<TriggerInfo>(
         $"""
         SELECT
            n.nspname                                    AS schema,
            c.relname                                    AS table_name,
            t.tgname                                     AS trigger_name,
            pg_catalog.pg_get_triggerdef(t.oid)           AS definition
         FROM pg_trigger t
         JOIN pg_class c ON c.oid = t.tgrelid
         JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE NOT t.tgisinternal
           AND {SCHEMA_FILTER}
         ORDER BY n.nspname, c.relname, t.tgname
         """,
         ct: cancellationToken
      );
   }

   /// <summary>
   ///    Retrieves all user-defined views.
   /// </summary>
   /// <param name="cancellationToken">A cancellation token.</param>
   /// <returns>The views.</returns>
   [PublicAPI]
   public async Task<IEnumerable<ViewInfo>> GetViewsAsync(CancellationToken cancellationToken = default)
   {
      return await _db.Dapper.QueryAsync<ViewInfo>(
         $"""
         SELECT
            n.nspname                                    AS schema,
            c.relname                                    AS name,
            pg_catalog.pg_get_viewdef(c.oid, true)       AS definition
         FROM pg_class c
         JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE c.relkind = 'v'
           AND {SCHEMA_FILTER}
         ORDER BY n.nspname, c.relname
         """,
         ct: cancellationToken
      );
   }

   // ========================================================================
   // Internal row types for queries that need grouping
   // ========================================================================

   private sealed record EnumTypeRow
   {
      public required string Schema { get; init; }
      public required string Name { get; init; }
      public required string Label { get; init; }
      public required float SortOrder { get; init; }
   }

   private sealed record CompositeTypeRow
   {
      public required string Schema { get; init; }
      public required string TypeName { get; init; }
      public required string AttributeName { get; init; }
      public required string DataType { get; init; }
   }

   private sealed record DomainTypeRow
   {
      public required string Schema { get; init; }
      public required string Name { get; init; }
      public required string BaseType { get; init; }
      public string? DefaultValue { get; init; }
      public required bool IsNotNull { get; init; }
   }

   private sealed record TableColumnRow
   {
      public required string Schema { get; init; }
      public required string TableName { get; init; }
      public required string ColumnName { get; init; }
      public required string DataType { get; init; }
      public required bool IsNullable { get; init; }
      public string? DefaultValue { get; init; }
      public required bool IsIdentity { get; init; }
      public string? IdentityGeneration { get; init; }
      public required int Ordinal { get; init; }
   }
}
