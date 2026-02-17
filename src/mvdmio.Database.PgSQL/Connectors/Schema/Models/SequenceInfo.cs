using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL sequence.
/// </summary>
/// <param name="Schema">The schema the sequence belongs to.</param>
/// <param name="Name">The name of the sequence.</param>
/// <param name="DataType">The data type of the sequence (e.g. "bigint", "integer").</param>
/// <param name="StartValue">The start value of the sequence.</param>
/// <param name="IncrementBy">The increment value.</param>
/// <param name="MinValue">The minimum value.</param>
/// <param name="MaxValue">The maximum value.</param>
/// <param name="CacheSize">The cache size.</param>
/// <param name="IsCyclic">Whether the sequence cycles when it reaches its limit.</param>
/// <param name="OwnedByTable">The table that owns this sequence, or null if not owned.</param>
/// <param name="OwnedByColumn">The column that owns this sequence, or null if not owned.</param>
[PublicAPI]
public sealed record SequenceInfo(
   string Schema,
   string Name,
   string DataType,
   long StartValue,
   long IncrementBy,
   long MinValue,
   long MaxValue,
   long CacheSize,
   bool IsCyclic,
   string? OwnedByTable,
   string? OwnedByColumn
);
