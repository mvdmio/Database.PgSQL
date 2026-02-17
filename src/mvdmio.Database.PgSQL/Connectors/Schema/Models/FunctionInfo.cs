using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL function or procedure.
/// </summary>
[PublicAPI]
public sealed class FunctionInfo
{
   /// <summary>The schema the function belongs to.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the function.</summary>
   public required string Name { get; init; }

   /// <summary>The argument signature used to uniquely identify the function.</summary>
   public required string IdentityArguments { get; init; }

   /// <summary>The full CREATE OR REPLACE FUNCTION/PROCEDURE definition as returned by pg_get_functiondef().</summary>
   public required string Definition { get; init; }
}
