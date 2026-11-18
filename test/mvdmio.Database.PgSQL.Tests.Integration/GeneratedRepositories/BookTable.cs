using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

[Table("public.generated_books")]
public partial class BookTable
{
   [PrimaryKey]
   [Generated]
   public long BookId { get; set; }

   [Unique]
   public string Title { get; set; } = string.Empty;

   public long? AuthorId { get; set; }

   public long? EditorId { get; set; }

   private AuthorRelation? Author { get; set; }
   private EditorRelation? Editor { get; set; }
   private List<BookTagsRelation> BookTags { get; set; } = [];

   private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AuthorId, y => y.AuthorId),
      ];
   }

   private class EditorRelation : RelationDefinition<BookTable, AuthorTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.EditorId, y => y.AuthorId),
      ];
   }

   private class BookTagsRelation : RelationDefinition<BookTable, BookTagTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.BookId, y => y.BookId),
      ];
   }
}
