using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL view.
/// </summary>
/// <param name="Schema">The schema the view belongs to.</param>
/// <param name="Name">The name of the view.</param>
/// <param name="Definition">The view query definition as returned by pg_get_viewdef().</param>
[PublicAPI]
public sealed record ViewInfo(string Schema, string Name, string Definition);
