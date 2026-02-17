using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL enum type.
/// </summary>
[PublicAPI]
public sealed class EnumTypeInfo
{
   /// <summary>The schema the enum type belongs to.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the enum type.</summary>
   public required string Name { get; init; }

   /// <summary>The enum labels in order.</summary>
   public required IReadOnlyList<string> Labels { get; init; }
}
