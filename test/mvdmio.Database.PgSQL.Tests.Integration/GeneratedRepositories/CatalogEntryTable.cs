using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    The target side of the nullable-unique-relation-target shape: a <see cref="Sku" /> marked <see cref="UniqueAttribute" />
///    that can still hold null. PostgreSQL's real <c>UNIQUE</c> constraint over it admits any number of nulls, which is
///    what makes <see cref="CatalogItemTable" />'s relation against it honest cover rather than an assumption. A
///    dedicated pair rather than a variation on <see cref="AuthorTable" />/<see cref="BookTable" />, so those tables'
///    existing assertions stay untouched.
/// </summary>
[Table("public.generated_catalog_entries")]
public partial class CatalogEntryTable
{
   [PrimaryKey]
   [Generated]
   public long EntryId { get; set; }

   [Unique]
   public string? Sku { get; set; }
}
