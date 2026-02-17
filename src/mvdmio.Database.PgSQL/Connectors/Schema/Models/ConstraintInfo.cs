using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL table constraint.
/// </summary>
[PublicAPI]
public sealed class ConstraintInfo
{
   /// <summary>The schema of the table the constraint belongs to.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the table the constraint belongs to.</summary>
   public required string TableName { get; init; }

   /// <summary>The name of the constraint.</summary>
   public required string ConstraintName { get; init; }

   /// <summary>The type of constraint: "p" (primary key), "f" (foreign key), "u" (unique), "c" (check), "x" (exclusion).</summary>
   public string? ConstraintType { get; init; }

   /// <summary>The constraint definition as returned by pg_get_constraintdef().</summary>
   public required string Definition { get; init; }
}
