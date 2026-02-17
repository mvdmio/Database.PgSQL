using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL table constraint.
/// </summary>
/// <param name="Schema">The schema of the table the constraint belongs to.</param>
/// <param name="TableName">The name of the table the constraint belongs to.</param>
/// <param name="ConstraintName">The name of the constraint.</param>
/// <param name="ConstraintType">The type of constraint: "p" (primary key), "f" (foreign key), "u" (unique), "c" (check), "x" (exclusion).</param>
/// <param name="Definition">The constraint definition as returned by pg_get_constraintdef().</param>
[PublicAPI]
public sealed record ConstraintInfo(
   string Schema,
   string TableName,
   string ConstraintName,
   string ConstraintType,
   string Definition
);
