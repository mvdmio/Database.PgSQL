using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

[Table("public.odata_books")]
public partial class BookTable
{
   [PrimaryKey]
   [Generated]
   public long BookId { get; set; }

   [Unique]
   public string Title { get; set; } = string.Empty;

   public long? AuthorId { get; set; }

   private AuthorRelation? Author { get; set; }

   private class AuthorRelation : RelationDefinition<BookTable, AuthorTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AuthorId, y => y.AuthorId),
      ];
   }
}
