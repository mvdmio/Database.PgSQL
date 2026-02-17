using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL extension installed in the database.
/// </summary>
[PublicAPI]
public sealed class ExtensionInfo
{
   /// <summary>The name of the extension (e.g. "uuid-ossp").</summary>
   public required string Name { get; init; }

   /// <summary>The schema the extension is installed in.</summary>
   public required string Schema { get; init; }

   /// <summary>The version of the extension.</summary>
   public required string Version { get; init; }
}
