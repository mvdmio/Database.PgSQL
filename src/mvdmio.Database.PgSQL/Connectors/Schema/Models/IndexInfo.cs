using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL index that is not backing a constraint.
/// </summary>
/// <param name="Schema">The schema of the table the index belongs to.</param>
/// <param name="TableName">The name of the table the index belongs to.</param>
/// <param name="IndexName">The name of the index.</param>
/// <param name="Definition">The full CREATE INDEX statement as returned by pg_get_indexdef().</param>
[PublicAPI]
public sealed record IndexInfo(string Schema, string TableName, string IndexName, string Definition);
