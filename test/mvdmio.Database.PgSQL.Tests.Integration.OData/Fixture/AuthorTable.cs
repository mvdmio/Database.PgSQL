using mvdmio.Database.PgSQL.Attributes;
using mvdmio.Database.PgSQL.Relations;

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
