using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL table.
/// </summary>
[PublicAPI]
public sealed class TableInfo
{
   /// <summary>The schema the table belongs to.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the table.</summary>
   public required string Name { get; init; }

   /// <summary>The columns of the table.</summary>
   public required IReadOnlyList<ColumnInfo> Columns { get; init; }
}

/// <summary>
///    Represents a column in a PostgreSQL table.
/// </summary>
[PublicAPI]
public sealed class ColumnInfo
{
   /// <summary>The column name.</summary>
   public required string Name { get; init; }

   /// <summary>The full SQL data type (e.g. "character varying(255)", "bigint").</summary>
   public required string DataType { get; init; }

   /// <summary>Whether the column allows NULL values.</summary>
   public required bool IsNullable { get; init; }

   /// <summary>The default value expression, or null if none.</summary>
   public string? DefaultValue { get; init; }

   /// <summary>Whether the column is an identity column.</summary>
   public required bool IsIdentity { get; init; }

   /// <summary>The identity generation type ("ALWAYS" or "BY DEFAULT"), or null if not an identity column.</summary>
   public string? IdentityGeneration { get; init; }
}
