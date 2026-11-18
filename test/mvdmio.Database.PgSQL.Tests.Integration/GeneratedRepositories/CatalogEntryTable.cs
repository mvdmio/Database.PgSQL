using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_catalog_entries")]
public partial class CatalogEntryTable
{
   [PrimaryKey]
   [Generated]
   public long EntryId { get; set; }

   [Unique]
   public string? Sku { get; set; }
}
