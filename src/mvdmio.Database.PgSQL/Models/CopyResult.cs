using mvdmio.Database.PgSQL.Connectors;
using mvdmio.Database.PgSQL.Connectors.Bulk;

namespace mvdmio.Database.PgSQL.Models;

/// <summary>
/// Result object for <see cref="BulkConnector.CopyAsync{T}"/>.
/// </summary>
public class CopyResult
{
   /// <summary>
   ///   The amount of rows provided for copy.
   /// </summary>
   public required long RowsProvided { get; set; }

   /// <summary>
   ///   The amount of rows written to the database successfully.
   /// </summary>
   public required long RowsWritten { get; set; }

   /// <summary>
   ///   The errors that occurred while writing.
   /// </summary>
   public required IEnumerable<Exception> Errors { get; set; }
}