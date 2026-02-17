using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL index that is not backing a constraint.
/// </summary>
[PublicAPI]
public sealed class IndexInfo
{
   /// <summary>The schema of the table the index belongs to.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the table the index belongs to.</summary>
   public required string TableName { get; init; }

   /// <summary>The name of the index.</summary>
   public required string IndexName { get; init; }

   /// <summary>The full CREATE INDEX statement as returned by pg_get_indexdef().</summary>
   public required string Definition { get; init; }
}
