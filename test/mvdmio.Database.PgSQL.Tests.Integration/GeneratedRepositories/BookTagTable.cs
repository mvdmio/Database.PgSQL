using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    The join table of a many-to-many. It is an ordinary table definition with a relation at each side, which is why
///    many-to-many needs no concept of its own.
/// </summary>
[Table("public.generated_book_tags")]
public partial class BookTagTable
{
   [PrimaryKey]
   [Generated]
   public long BookTagId { get; set; }

   public long BookId { get; set; }

   public long TagId { get; set; }

   private BookRelation? Book { get; set; }
   private TagRelation? Tag { get; set; }

   private class BookRelation : RelationDefinition<BookTagTable, BookTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.BookId, y => y.BookId),
      ];
   }

   private class TagRelation : RelationDefinition<BookTagTable, TagTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.TagId, y => y.TagId),
      ];
   }
}
