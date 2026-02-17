using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL trigger.
/// </summary>
[PublicAPI]
public sealed class TriggerInfo
{
   /// <summary>The schema of the table the trigger is defined on.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the table the trigger is defined on.</summary>
   public required string TableName { get; init; }

   /// <summary>The name of the trigger.</summary>
   public required string TriggerName { get; init; }

   /// <summary>The full trigger definition as returned by pg_get_triggerdef().</summary>
   public required string Definition { get; init; }
}
