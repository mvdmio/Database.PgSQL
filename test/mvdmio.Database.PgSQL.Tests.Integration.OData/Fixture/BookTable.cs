using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    The other half of the relation-bearing pair. Its foreign key is nullable, so a row whose key points nowhere makes
///    an expansion that finds nothing observable.
/// </summary>
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
