using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL function or procedure.
/// </summary>
/// <param name="Schema">The schema the function belongs to.</param>
/// <param name="Name">The name of the function.</param>
/// <param name="IdentityArguments">The argument signature used to uniquely identify the function.</param>
/// <param name="Definition">The full CREATE OR REPLACE FUNCTION/PROCEDURE definition as returned by pg_get_functiondef().</param>
[PublicAPI]
public sealed record FunctionInfo(string Schema, string Name, string IdentityArguments, string Definition);
