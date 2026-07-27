using mvdmio.Database.PgSQL.Attributes;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    A table definition that relates to itself in both directions and to another table, so that a hierarchy and an
///    ordinary parent-to-children relation are both observable.
/// </summary>
[Table("public.generated_authors")]
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
