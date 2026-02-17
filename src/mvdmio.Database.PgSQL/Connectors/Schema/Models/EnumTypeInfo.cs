using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL enum type.
/// </summary>
/// <param name="Schema">The schema the enum type belongs to.</param>
/// <param name="Name">The name of the enum type.</param>
/// <param name="Labels">The enum labels in order.</param>
[PublicAPI]
public sealed record EnumTypeInfo(string Schema, string Name, IReadOnlyList<string> Labels);
