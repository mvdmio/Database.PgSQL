namespace mvdmio.Database.PgSQL.Connectors.Bulk;

/// <summary>
/// Represents the result of an insert or update operation for a single item.
/// </summary>
/// <typeparam name="T">The type of the item that was inserted or updated.</typeparam>
public sealed class InsertOrUpdateResult<T>
{
   /// <summary>
   /// The item that was inserted or updated.
   /// </summary>
   public required T Item { get; set; }

   /// <summary>
   /// Indicates whether the item was inserted as a new row.
   /// </summary>
   public required bool IsInserted { get; set; }

   /// <summary>
   /// Indicates whether an existing row was updated.
   /// </summary>
   public required bool IsUpdated { get; set; }
}
