namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

public abstract class RelationConformanceTestBase : ODataTestBase
{
   private static readonly (string Name, string? MentorName)[] _authors = [
      ("tolkien", null),
      ("lewis", "tolkien"),
      ("pratchett", "lewis"),
      ("gaiman", "pratchett")
   ];

   private static readonly (string Title, string? AuthorName)[] _books = [
      ("hobbit", "tolkien"),
      ("silmarillion", "tolkien"),
      ("narnia", "lewis"),
      ("orphan", null)
   ];

   protected AuthorRepository Authors { get; private set; } = null!;
   protected BookRepository Books { get; private set; } = null!;

   protected RelationConformanceTestBase(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      Authors = new AuthorRepository(Db);
      Books = new BookRepository(Db);

      var authorIds = new Dictionary<string, long>(StringComparer.Ordinal);

      foreach (var (name, mentorName) in _authors)
      {
         var mentorId = mentorName is null ? (long?)null : authorIds[mentorName];
         var author = await Authors.CreateAsync(new CreateAuthorCommand { Name = name, MentorId = mentorId }, CancellationToken);

         authorIds[name] = author.AuthorId;
      }

      foreach (var (title, authorName) in _books)
      {
         var authorId = authorName is null ? (long?)null : authorIds[authorName];

         await Books.CreateAsync(new CreateBookCommand { Title = title, AuthorId = authorId }, CancellationToken);
      }
   }

   protected AppliedQuery ApplyToAuthors(string queryString)
   {
      return ODataQuery.Apply(Authors.Query(), queryString, ODataConfiguration.RelationModel);
   }

   protected AppliedQuery ApplyToBooks(string queryString)
   {
      return ODataQuery.Apply(Books.Query(), queryString, ODataConfiguration.RelationModel);
   }

   protected static async Task<IReadOnlyList<string>> TitlesAsync(AppliedQuery applied)
   {
      var rows = await applied.RowsAsync<BookData>(CancellationToken);

      return rows.Select(x => x.Title).ToList();
   }

   protected static async Task<IReadOnlyList<string>> NamesAsync(AppliedQuery applied)
   {
      var rows = await applied.RowsAsync<AuthorData>(CancellationToken);

      return rows.Select(x => x.Name).ToList();
   }
}
