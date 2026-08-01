using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

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

   private MentorRelation? Mentor { get; set; }
   private List<MenteesRelation> Mentees { get; set; } = [];
   private List<BooksRelation> Books { get; set; } = [];

   private class MentorRelation : RelationDefinition<AuthorTable, AuthorTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.MentorId, y => y.AuthorId),
      ];
   }

   private class MenteesRelation : RelationDefinition<AuthorTable, AuthorTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AuthorId, y => y.MentorId),
      ];
   }

   private class BooksRelation : RelationDefinition<AuthorTable, BookTable>
   {
      public override IReadOnlyList<RelationKey> Keys => [
         Key(x => x.AuthorId, y => y.AuthorId),
      ];
   }
}
