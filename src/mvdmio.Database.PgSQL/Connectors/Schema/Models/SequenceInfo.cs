using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL sequence.
/// </summary>
[PublicAPI]
public sealed record SequenceInfo
{
   /// <summary>The schema the sequence belongs to.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the sequence.</summary>
   public required string Name { get; init; }

   /// <summary>The data type of the sequence (e.g. "bigint", "integer").</summary>
   public required string DataType { get; init; }

   /// <summary>The start value of the sequence.</summary>
   public required long StartValue { get; init; }

   /// <summary>The increment value.</summary>
   public required long IncrementBy { get; init; }

   /// <summary>The minimum value.</summary>
   public required long MinValue { get; init; }

   /// <summary>The maximum value.</summary>
   public required long MaxValue { get; init; }

   /// <summary>The cache size.</summary>
   public required long CacheSize { get; init; }

   /// <summary>Whether the sequence cycles when it reaches its limit.</summary>
   public required bool IsCyclic { get; init; }

   /// <summary>The table that owns this sequence, or null if not owned.</summary>
   public string? OwnedByTable { get; init; }

   /// <summary>The column that owns this sequence, or null if not owned.</summary>
   public string? OwnedByColumn { get; init; }
}
