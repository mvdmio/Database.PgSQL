using mvdmio.Database.PgSQL.Attributes;

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

   [Relation(nameof(BookId))]
   public BookTable? Book { get; set; }

   [Relation(nameof(TagId))]
   public TagTable? Tag { get; set; }
}
