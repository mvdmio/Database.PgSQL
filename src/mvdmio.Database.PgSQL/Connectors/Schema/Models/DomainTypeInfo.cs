using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL domain type.
/// </summary>
/// <param name="Schema">The schema the domain type belongs to.</param>
/// <param name="Name">The name of the domain type.</param>
/// <param name="BaseType">The base data type of the domain.</param>
/// <param name="DefaultValue">The default value expression, or null if none.</param>
/// <param name="IsNotNull">Whether the domain has a NOT NULL constraint.</param>
/// <param name="CheckConstraints">The check constraint expressions defined on the domain.</param>
[PublicAPI]
public sealed record DomainTypeInfo(
   string Schema,
   string Name,
   string BaseType,
   string? DefaultValue,
   bool IsNotNull,
   IReadOnlyList<string> CheckConstraints
);
