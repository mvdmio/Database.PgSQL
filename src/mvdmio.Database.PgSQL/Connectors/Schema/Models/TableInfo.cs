using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL table.
/// </summary>
/// <param name="Schema">The schema the table belongs to.</param>
/// <param name="Name">The name of the table.</param>
/// <param name="Columns">The columns of the table.</param>
[PublicAPI]
public sealed record TableInfo(string Schema, string Name, IReadOnlyList<ColumnInfo> Columns);

/// <summary>
///    Represents a column in a PostgreSQL table.
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="DataType">The full SQL data type (e.g. "character varying(255)", "bigint").</param>
/// <param name="IsNullable">Whether the column allows NULL values.</param>
/// <param name="DefaultValue">The default value expression, or null if none.</param>
/// <param name="IsIdentity">Whether the column is an identity column.</param>
/// <param name="IdentityGeneration">The identity generation type ("ALWAYS" or "BY DEFAULT"), or null if not an identity column.</param>
[PublicAPI]
public sealed record ColumnInfo(
   string Name,
   string DataType,
   bool IsNullable,
   string? DefaultValue,
   bool IsIdentity,
   string? IdentityGeneration
);
