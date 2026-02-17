using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL domain type.
/// </summary>
[PublicAPI]
public sealed class DomainTypeInfo
{
   /// <summary>The schema the domain type belongs to.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the domain type.</summary>
   public required string Name { get; init; }

   /// <summary>The base data type of the domain.</summary>
   public required string BaseType { get; init; }

   /// <summary>The default value expression, or null if none.</summary>
   public string? DefaultValue { get; init; }

   /// <summary>Whether the domain has a NOT NULL constraint.</summary>
   public required bool IsNotNull { get; init; }

   /// <summary>The check constraint expressions defined on the domain.</summary>
   public required IReadOnlyList<string> CheckConstraints { get; init; }
}
