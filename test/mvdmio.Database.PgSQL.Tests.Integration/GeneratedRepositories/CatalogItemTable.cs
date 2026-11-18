using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_catalog_items")]
public partial class CatalogItemTable
{
   [PrimaryKey]
   [Generated]
   public long ItemId { get; set; }

   public string Sku { get; set; } = string.Empty;

   private EntryRelation? Entry { get; set; }

   private class EntryRelation : RelationDefinition<CatalogItemTable, CatalogEntryTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.Sku, y => y.Sku),
      ];
   }
}
