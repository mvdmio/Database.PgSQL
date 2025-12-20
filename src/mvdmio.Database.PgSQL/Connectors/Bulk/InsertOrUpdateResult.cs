namespace mvdmio.Database.PgSQL.Connectors.Bulk;

public class InsertOrUpdateResult<T>
{
   public required T Item { get; set; }
   public required bool IsInserted { get; set; }
   public required bool IsUpdated { get; set; }
}
