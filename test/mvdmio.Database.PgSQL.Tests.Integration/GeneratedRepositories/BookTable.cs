using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    A table definition with two relations at the same target and two nullable foreign keys, so that outer-join
///    semantics are observable on a row whose key points nowhere.
/// </summary>
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

   [Relation(nameof(AuthorId))]
   public AuthorTable? Author { get; set; }

   [Relation(nameof(EditorId))]
   public AuthorTable? Editor { get; set; }

   [Relation(nameof(BookTagTable.BookId))]
   public List<BookTagTable> BookTags { get; set; } = [];
}
