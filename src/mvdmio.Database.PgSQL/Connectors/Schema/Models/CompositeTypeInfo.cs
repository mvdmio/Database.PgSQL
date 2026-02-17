using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL composite type.
/// </summary>
[PublicAPI]
public sealed class CompositeTypeInfo
{
   /// <summary>The schema the composite type belongs to.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the composite type.</summary>
   public required string Name { get; init; }

   /// <summary>The attributes (columns) of the composite type.</summary>
   public required IReadOnlyList<CompositeTypeAttributeInfo> Attributes { get; init; }
}

/// <summary>
///    Represents a single attribute of a composite type.
/// </summary>
[PublicAPI]
public sealed class CompositeTypeAttributeInfo
{
   /// <summary>The attribute name.</summary>
   public required string Name { get; init; }

   /// <summary>The SQL data type of the attribute.</summary>
   public required string DataType { get; init; }
}
