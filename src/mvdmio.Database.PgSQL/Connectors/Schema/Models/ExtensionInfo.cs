using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL extension installed in the database.
/// </summary>
/// <param name="Name">The name of the extension (e.g. "uuid-ossp").</param>
/// <param name="Schema">The schema the extension is installed in.</param>
/// <param name="Version">The version of the extension.</param>
[PublicAPI]
public sealed record ExtensionInfo(string Name, string Schema, string Version);
