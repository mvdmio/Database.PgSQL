using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL trigger.
/// </summary>
/// <param name="Schema">The schema of the table the trigger is defined on.</param>
/// <param name="TableName">The name of the table the trigger is defined on.</param>
/// <param name="TriggerName">The name of the trigger.</param>
/// <param name="Definition">The full trigger definition as returned by pg_get_triggerdef().</param>
[PublicAPI]
public sealed record TriggerInfo(string Schema, string TableName, string TriggerName, string Definition);
