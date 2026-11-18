using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_tags")]
public partial class TagTable
{
   [PrimaryKey]
   [Generated]
   public long TagId { get; set; }

   [Unique]
   public string Label { get; set; } = string.Empty;

   private List<BookTagsRelation> BookTags { get; set; } = [];

   private class BookTagsRelation : RelationDefinition<TagTable, BookTagTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.TagId, y => y.TagId),
      ];
   }
}
