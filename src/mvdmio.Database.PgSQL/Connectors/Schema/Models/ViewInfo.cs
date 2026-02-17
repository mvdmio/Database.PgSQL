using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Schema.Models;

/// <summary>
///    Represents a PostgreSQL view.
/// </summary>
[PublicAPI]
public sealed class ViewInfo
{
   /// <summary>The schema the view belongs to.</summary>
   public required string Schema { get; init; }

   /// <summary>The name of the view.</summary>
   public required string Name { get; init; }

   /// <summary>The view query definition as returned by pg_get_viewdef().</summary>
   public required string Definition { get; init; }
}
