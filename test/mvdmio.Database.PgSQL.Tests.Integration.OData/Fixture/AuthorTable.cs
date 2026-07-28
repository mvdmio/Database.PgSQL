using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    One half of the relation-bearing pair the expansion tests query through. It relates to itself in both directions
///    and to books, so a to-one expansion, a to-many expansion, a nested expansion and <c>$levels</c> over a hierarchy
///    all have something to reach.
/// </summary>
/// <remarks>
///    Deliberately shaped like the main integration suite's <c>AuthorTable</c>, so the two suites can be read against
///    each other. Every property type here has a direct EDM equivalent, so a model-building failure could only come from
///    the relations.
/// </remarks>
[Table("public.odata_authors")]
public partial class AuthorTable
{
   [PrimaryKey]
   [Generated]
   public long AuthorId { get; set; }

   [Unique]
   public string Name { get; set; } = string.Empty;

   public long? MentorId { get; set; }

   [Relation(nameof(MentorId))]
   public AuthorTable? Mentor { get; set; }

   [Relation(nameof(MentorId))]
   public List<AuthorTable> Mentees { get; set; } = [];

   [Relation(nameof(BookTable.AuthorId))]
   public List<BookTable> Books { get; set; } = [];
}
