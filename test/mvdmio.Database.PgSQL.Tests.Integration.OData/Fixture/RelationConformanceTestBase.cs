namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    Seeds the author-and-book graph and applies query strings against <see cref="ODataConfiguration.RelationModel" />.
///    Parallel to <see cref="SampleConformanceTestBase" />, and for the same reason: every test goes through a generated
///    repository's <c>Query()</c> rather than the <c>Linq</c> adapter directly, because that is the seam a consumer
///    calls.
/// </summary>
public abstract class RelationConformanceTestBase : ODataTestBase
{
   /// <summary>
   ///    Chosen so that every assertion discriminates: the mentor chain is four deep, so <c>$levels=2</c> stops
   ///    somewhere an unbounded walk would not; two authors have no books, so an empty expanded collection is
   ///    observable; and one book has no author, so an expansion across a relation that finds nothing is observable.
   /// </summary>
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

   /// <summary>Applies a query string to the author repository's queryable using the recommended settings.</summary>
   protected AppliedQuery ApplyToAuthors(string queryString)
   {
      return ODataQuery.Apply(Authors.Query(), queryString, ODataConfiguration.RelationModel);
   }

   /// <summary>Applies a query string to the book repository's queryable using the recommended settings.</summary>
   protected AppliedQuery ApplyToBooks(string queryString)
   {
      return ODataQuery.Apply(Books.Query(), queryString, ODataConfiguration.RelationModel);
   }

   /// <summary>The titles an applied query returned, in the order the database gave them. Titles are unique here.</summary>
   protected static async Task<IReadOnlyList<string>> TitlesAsync(AppliedQuery applied)
   {
      var rows = await applied.RowsAsync<BookData>(CancellationToken);

      return rows.Select(x => x.Title).ToList();
   }

   /// <summary>The names an applied query returned, in the order the database gave them. Names are unique here.</summary>
   protected static async Task<IReadOnlyList<string>> NamesAsync(AppliedQuery applied)
   {
      var rows = await applied.RowsAsync<AuthorData>(CancellationToken);

      return rows.Select(x => x.Name).ToList();
   }

   /// <summary>
   ///    The row an expanded to-one navigation property produced, or null when the expansion found nothing.
   /// </summary>
   protected static IDictionary<string, object?>? ExpandedRow(IDictionary<string, object?> row, string propertyName)
   {
      return (IDictionary<string, object?>?)ValueOf(row, propertyName);
   }

   /// <summary>The rows an expanded to-many navigation property produced.</summary>
   protected static IReadOnlyList<IDictionary<string, object?>> ExpandedRows(IDictionary<string, object?> row, string propertyName)
   {
      return (IReadOnlyList<IDictionary<string, object?>>?)ValueOf(row, propertyName) ?? [];
   }

   private static object? ValueOf(IDictionary<string, object?> row, string propertyName)
   {
      if (!row.TryGetValue(propertyName, out var value))
         throw new InvalidOperationException($"'{propertyName}' is not one of the projected values: {string.Join(", ", row.Keys)}.");

      return value;
   }
}
