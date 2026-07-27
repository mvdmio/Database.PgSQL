using JetBrains.Annotations;

namespace mvdmio.Database.PgSQL.Connectors.Linq;

/// <summary>
///    The PostgreSQL dialect the query surface generates SQL for.
/// </summary>
[PublicAPI]
public enum PostgresDialect
{
   /// <summary>
   ///    The newest dialect the query surface knows about. This is the default.
   /// </summary>
   Latest = 0,

   /// <summary>PostgreSQL 9.2.</summary>
   V92,

   /// <summary>PostgreSQL 9.3.</summary>
   V93,

   /// <summary>PostgreSQL 9.5.</summary>
   V95,

   /// <summary>PostgreSQL 13.</summary>
   V13,

   /// <summary>PostgreSQL 15.</summary>
   V15,

   /// <summary>PostgreSQL 18.</summary>
   V18
}
