using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL composite type.
/// </summary>
/// <param name="Schema">The schema the composite type belongs to.</param>
/// <param name="Name">The name of the composite type.</param>
/// <param name="Attributes">The attributes (columns) of the composite type.</param>
[PublicAPI]
public sealed record CompositeTypeInfo(string Schema, string Name, IReadOnlyList<CompositeTypeAttributeInfo> Attributes);

/// <summary>
///    Represents a single attribute of a composite type.
/// </summary>
/// <param name="Name">The attribute name.</param>
/// <param name="DataType">The SQL data type of the attribute.</param>
[PublicAPI]
public sealed record CompositeTypeAttributeInfo(string Name, string DataType);
