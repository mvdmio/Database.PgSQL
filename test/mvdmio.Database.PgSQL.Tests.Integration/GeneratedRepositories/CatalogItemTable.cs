using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    The declaring side of the nullable-unique-relation-target shape: a not-null <see cref="Sku" /> paired against
///    <see cref="CatalogEntryTable" />'s nullable <c>[Unique]</c> column of the same name. The pairing compiles with no
///    change to <c>Key(...)</c>, which is itself the proof that the analyzer's PGSQL0035 rule admits it.
/// </summary>
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
