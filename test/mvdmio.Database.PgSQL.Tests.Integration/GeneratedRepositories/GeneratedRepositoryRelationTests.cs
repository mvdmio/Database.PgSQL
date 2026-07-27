using AwesomeAssertions;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    Covers relations end to end: the parser, the emitted mapping and mirrored properties, the module initializer, the
///    adapter, the decorator, the include rewriter, the query provider and PostgreSQL. <see cref="TestBase" /> opens a
///    transaction for every test, so each of these also proves a relation query binds to the ambient transaction.
/// </summary>
public class GeneratedRepositoryRelationTests : TestBase
{
   private readonly TestFixture _fixture;

   private AuthorRepository _authors = null!;
   private BookRepository _books = null!;
   private TagRepository _tags = null!;
   private BookTagRepository _bookTags = null!;

   private long _tolkienId;
   private long _lewisId;
   private long _pratchettId;
   private long _hobbitId;
   private long _narniaId;

   public GeneratedRepositoryRelationTests(TestFixture fixture)
      : base(fixture)
   {
      _fixture = fixture;
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _authors = new AuthorRepository(Db);
      _books = new BookRepository(Db);
      _tags = new TagRepository(Db);
      _bookTags = new BookTagRepository(Db);

      _tolkienId = await CreateAuthorAsync("tolkien", mentorId: null);
      _lewisId = await CreateAuthorAsync("lewis", _tolkienId);
      _pratchettId = await CreateAuthorAsync("pratchett", _lewisId);

      _hobbitId = await CreateBookAsync("hobbit", _tolkienId, _lewisId);
      await CreateBookAsync("silmarillion", _tolkienId, editorId: null);
      _narniaId = await CreateBookAsync("narnia", _lewisId, _tolkienId);
      await CreateBookAsync("orphan", authorId: null, editorId: null);

      var fantasyId = await CreateTagAsync("fantasy");
      var classicId = await CreateTagAsync("classic");

      await CreateBookTagAsync(_hobbitId, fantasyId);
      await CreateBookTagAsync(_hobbitId, classicId);
      await CreateBookTagAsync(_narniaId, fantasyId);
   }

   [Fact]
   public async Task Query_FilteringAndOrderingAcrossARelationToOneRow_NeedsNoExtraApi()
   {
      var byAuthor = await _books.Query()
         .Where(x => x.Author!.Name == "tolkien")
         .OrderBy(x => x.Title)
         .ToListAsync(CancellationToken);

      byAuthor.Select(x => x.Title).Should().Equal("hobbit", "silmarillion");

      var byEditorName = await _books.Query()
         .Where(x => x.Editor!.Name != null)
         .OrderByDescending(x => x.Editor!.Name)
         .ThenBy(x => x.Title)
         .ToListAsync(CancellationToken);

      byEditorName.Select(x => x.Title).Should().Equal("narnia", "hobbit");
   }

   [Fact]
   public async Task Query_FilteringAcrossTwoHopsOfRelations_ReachesTheGrandparentsColumn()
   {
      var mentoredByTolkien = await _books.Query()
         .Where(x => x.Author!.Mentor!.Name == "tolkien")
         .ToListAsync(CancellationToken);

      mentoredByTolkien.Select(x => x.Title).Should().Equal("narnia");
   }

   [Fact]
   public async Task Query_FilteringAcrossARelationToManyRows_ReturnsTheMatchingRows()
   {
      var authorsOfNarnia = await _authors.Query()
         .Where(x => x.Books.Any(book => book.Title == "narnia"))
         .ToListAsync(CancellationToken);

      authorsOfNarnia.Select(x => x.Name).Should().Equal("lewis");

      var taggedClassic = await _books.Query()
         .Where(x => x.BookTags.Any(bookTag => bookTag.Tag!.Label == "classic"))
         .ToListAsync(CancellationToken);

      taggedClassic.Select(x => x.Title).Should().Equal("hobbit");
   }

   [Fact]
   public async Task Query_WithAForeignKeyPointingNowhere_ReturnsTheRowUntilAPredicateLandsOnTheFarSide()
   {
      var all = await _books.Query().ToListAsync(CancellationToken);

      all.Select(x => x.Title).Should().Contain("orphan");

      var withAnAuthor = await _books.Query()
         .Where(x => x.Author!.Name != null)
         .ToListAsync(CancellationToken);

      withAnAuthor.Select(x => x.Title).Should().NotContain("orphan");
   }

   [Fact]
   public async Task Query_WithoutMaterialization_LeavesRelationPropertiesUnloaded()
   {
      var book = await _books.Query().Where(x => x.Title == "hobbit").SingleAsync(CancellationToken);

      book.Author.Should().BeNull();
      book.BookTags.Should().BeEmpty();

      var author = await _authors.Query().Where(x => x.Name == "tolkien").SingleAsync(CancellationToken);

      author.Books.Should().BeEmpty();
   }

   [Fact]
   public async Task Query_MaterializingARelationToOneRow_PopulatesTheMirroredProperty()
   {
      var books = await _books.Query()
         .Include(x => x.Author)
         .OrderBy(x => x.Title)
         .ToListAsync(CancellationToken);

      books.Single(x => x.Title == "hobbit").Author!.Name.Should().Be("tolkien");
      books.Single(x => x.Title == "orphan").Author.Should().BeNull();
   }

   [Fact]
   public async Task Query_MaterializingARelationToManyRows_PopulatesTheCollectionAndLeavesItEmptyWhenThereAreNone()
   {
      var authors = await _authors.Query()
         .Include(x => x.Books)
         .ToListAsync(CancellationToken);

      authors.Single(x => x.Name == "tolkien").Books.Select(x => x.Title).Should().BeEquivalentTo("hobbit", "silmarillion");
      authors.Single(x => x.Name == "pratchett").Books.Should().BeEmpty();
   }

   [Fact]
   public async Task Query_ChainingMaterializationThroughARelationToOneRow_PopulatesBothLevels()
   {
      var narnia = await _books.Query()
         .Include(x => x.Author)
         .ThenInclude(x => x.Mentor)
         .Where(x => x.Title == "narnia")
         .SingleAsync(CancellationToken);

      narnia.Author!.Name.Should().Be("lewis");
      narnia.Author.Mentor!.Name.Should().Be("tolkien");
   }

   [Fact]
   public async Task Query_ChainingMaterializationThroughARelationToManyRows_PopulatesBothLevels()
   {
      var tolkien = await _authors.Query()
         .Include(x => x.Books)
         .ThenInclude(x => x.Editor)
         .Where(x => x.Name == "tolkien")
         .SingleAsync(CancellationToken);

      tolkien.Books.Single(x => x.Title == "hobbit").Editor!.Name.Should().Be("lewis");
      tolkien.Books.Single(x => x.Title == "silmarillion").Editor.Should().BeNull();
   }

   /// <remarks>
   ///    The provider rejects this, because its own chaining marker does not survive an intervening operator. The
   ///    library records both halves and emits them contiguously at execution time, so the operator is free to sit
   ///    between them — it only has to be named as the marker again, which is what the cast does.
   /// </remarks>
   [Fact]
   public async Task Query_WithAnOperatorBetweenTheHalvesOfAChainedMaterialization_StillPopulatesBothLevels()
   {
      var withAnOperatorBetween = (IIncludedQueryable<BookData, AuthorData>)_books.Query()
         .Include(x => x.Author)
         .Where(x => x.Title == "narnia");

      var narnia = await withAnOperatorBetween
         .ThenInclude(x => x.Mentor)
         .SingleAsync(CancellationToken);

      narnia.Author!.Mentor!.Name.Should().Be("tolkien");
   }

   [Fact]
   public async Task Query_WithAFilteredMaterialization_LoadsOnlyTheScopedRows()
   {
      var tolkien = await _authors.Query()
         .Include(x => x.Books, books => books.Where(book => book.Title == "hobbit"))
         .Where(x => x.Name == "tolkien")
         .SingleAsync(CancellationToken);

      tolkien.Books.Select(x => x.Title).Should().Equal("hobbit");
   }

   /// <remarks>
   ///    A scoping lambda is consumer-written, so it may name another repository's query — and that query's decorator has
   ///    to be resolved against its own root before it reaches the provider, exactly as a predicate's would be. The
   ///    filter does not travel in the composed expression, so nothing else would have rewritten it.
   /// </remarks>
   [Fact]
   public async Task Query_WithAFilteredMaterializationNamingAnotherRepository_ScopesByTheTableItNames()
   {
      var tolkien = await _authors.Query()
         .Include(x => x.Books, books => books.Where(book => _bookTags.Query().Any(bookTag => bookTag.BookId == book.BookId)))
         .Where(x => x.Name == "tolkien")
         .SingleAsync(CancellationToken);

      tolkien.Books.Select(x => x.Title).Should().Equal("hobbit");
   }

   [Fact]
   public async Task Query_MaterializingASelfReference_PopulatesBothDirections()
   {
      var lewis = await _authors.Query()
         .Include(x => x.Mentor)
         .Where(x => x.Name == "lewis")
         .SingleAsync(CancellationToken);

      lewis.Mentor!.Name.Should().Be("tolkien");

      var mentees = await _authors.Query()
         .Include(x => x.Mentees)
         .Where(x => x.Name == "tolkien")
         .SingleAsync(CancellationToken);

      mentees.Mentees.Select(x => x.Name).Should().Equal("lewis");
   }

   [Fact]
   public async Task Query_TraversingAJoinTable_ReachesTheFarSideInBothDirections()
   {
      var hobbit = await _books.Query()
         .Include(x => x.BookTags)
         .ThenInclude(x => x.Tag)
         .Where(x => x.Title == "hobbit")
         .SingleAsync(CancellationToken);

      hobbit.BookTags.Select(x => x.Tag!.Label).Should().BeEquivalentTo("fantasy", "classic");

      var fantasy = await _tags.Query()
         .Include(x => x.BookTags)
         .ThenInclude(x => x.Book)
         .Where(x => x.Label == "fantasy")
         .SingleAsync(CancellationToken);

      fantasy.BookTags.Select(x => x.Book!.Title).Should().BeEquivalentTo("hobbit", "narnia");
   }

   [Fact]
   public async Task Query_WithMaterialization_WorksWithEveryAwaitingOperator()
   {
      (await _books.Query().Include(x => x.Author).OrderBy(x => x.Title).FirstAsync(CancellationToken)).Title.Should().Be("hobbit");
      (await _books.Query().Include(x => x.Author).Where(x => x.Title == "nobody").FirstOrDefaultAsync(CancellationToken)).Should().BeNull();
      (await _books.Query().Include(x => x.Author).Where(x => x.Title == "narnia").SingleAsync(CancellationToken)).Author!.Name.Should().Be("lewis");
      (await _books.Query().Include(x => x.Author).Where(x => x.Title == "nobody").SingleOrDefaultAsync(CancellationToken)).Should().BeNull();
      (await _books.Query().Include(x => x.Author).CountAsync(CancellationToken)).Should().Be(4);
      (await _books.Query().Include(x => x.Author).LongCountAsync(CancellationToken)).Should().Be(4L);
      (await _books.Query().Include(x => x.Author).AnyAsync(CancellationToken)).Should().BeTrue();
      (await _books.Query().Include(x => x.Author).ToListAsync(CancellationToken)).Should().HaveCount(4);

      _books.Query().Include(x => x.Author).OrderBy(x => x.Title).First().Title.Should().Be("hobbit");
      _books.Query().Include(x => x.Author).ToList().Should().HaveCount(4);
   }

   [Fact]
   public async Task Query_WithMaterialization_ConsumedAsAnAsyncStream_YieldsEveryRow()
   {
      var titles = new List<string>();

      await foreach (var book in (IAsyncEnumerable<BookData>)_books.Query().Include(x => x.Author).OrderBy(x => x.Title))
      {
         titles.Add(book.Title);
         book.Author?.Name.Should().NotBeNull();
      }

      titles.Should().Equal("hobbit", "narnia", "orphan", "silmarillion");
   }

   [Fact]
   public async Task Query_WithMaterialization_SeesUncommittedWritesMadeThroughTheDapperSurface()
   {
      await CreateBookAsync("children of hurin", _tolkienId, editorId: null);

      var tolkien = await _authors.Query()
         .Include(x => x.Books)
         .Where(x => x.Name == "tolkien")
         .SingleAsync(CancellationToken);

      tolkien.Books.Select(x => x.Title).Should().BeEquivalentTo("hobbit", "silmarillion", "children of hurin");
   }

   [Fact]
   public async Task Query_WithMaterializationComposedBeforeATransactionBegins_RunsInsideThatTransaction()
   {
      await using var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var authors = new AuthorRepository(connection);
      var books = new BookRepository(connection);

      var query = authors.Query().Include(x => x.Books).Where(x => x.Name == "uncommitted");

      await connection.BeginTransactionAsync(ct: CancellationToken);

      try
      {
         var author = await authors.CreateAsync(new CreateAuthorCommand { Name = "uncommitted" }, CancellationToken);
         await books.CreateAsync(new CreateBookCommand { Title = "uncommitted book", AuthorId = author.AuthorId }, CancellationToken);

         var rows = await query.ToListAsync(CancellationToken);

         rows.Should().ContainSingle();
         rows[0].Books.Select(x => x.Title).Should().Equal("uncommitted book");
      }
      finally
      {
         await connection.RollbackTransactionAsync(CancellationToken);
      }
   }

   [Fact]
   public async Task Query_WithMaterializationAndAnUntranslatableExpression_ThrowsQueryTranslationException()
   {
      var query = _books.Query().Include(x => x.Author).Where(x => IsUntranslatable(x.Title));

      var asyncFailure = await Record.ExceptionAsync(() => query.ToListAsync(CancellationToken));
      var syncFailure = Record.Exception(() => query.ToList());

      asyncFailure.Should().BeOfType<QueryTranslationException>();
      syncFailure.Should().BeOfType<QueryTranslationException>();
   }

   [Fact]
   public async Task Query_WithMaterializationThatFailsInTheDatabase_ThrowsQueryExceptionCarryingTheSql()
   {
      var zero = 0;

      var failure = await Record.ExceptionAsync(
         () => _books.Query().Include(x => x.Author).Where(x => x.BookId / zero == 1).ToListAsync(CancellationToken)
      );

      failure.Should().BeOfType<QueryException>();
      ((QueryException)failure!).Sql.Should().Contain("generated_books");
   }

   [Fact]
   public async Task Query_WithMaterializationEnumeratedAfterTheConnectionIsDisposed_ThrowsObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var query = new BookRepository(connection).Query().Include(x => x.Author);

      await connection.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() => query.ToListAsync(CancellationToken));
   }

   /// <remarks>
   ///    A detail statement re-derives its parents by re-running the query above it as a derived table, and that is what
   ///    a filter on the main query reaches: it decides which parents get detail rows, and does not narrow the detail
   ///    rows themselves — not even on a self-referencing hierarchy, where every row the filter excluded is a candidate
   ///    detail row. These assertions record what the database actually returns rather than what the design assumed, so
   ///    the day the provider's split-query strategy changes this test says so.
   /// </remarks>
   [Fact]
   public async Task Query_WithAMainQueryFilterAndAMaterializedHierarchy_ScopesTheParentsWithoutNarrowingTheDetailRows()
   {
      var roots = await _authors.Query()
         .Include(x => x.Mentees)
         .Where(x => x.MentorId == null)
         .ToListAsync(CancellationToken);

      roots.Select(x => x.Name).Should().Equal("tolkien");

      // lewis is a row the main query excluded, and it still arrives as tolkien's mentee.
      roots[0].Mentees.Select(x => x.Name).Should().Equal("lewis");

      var twoLevels = await _authors.Query()
         .Include(x => x.Mentees)
         .ThenInclude(x => x.Mentees)
         .Where(x => x.MentorId == null)
         .ToListAsync(CancellationToken);

      twoLevels[0].Mentees.Single().Mentees.Select(x => x.Name).Should().Equal("pratchett");

      // Narrowing the detail rows themselves is what the filtered overload is for, and only it does that.
      var scoped = await _authors.Query()
         .Include(x => x.Mentees, mentees => mentees.Where(mentee => mentee.Name == "nobody"))
         .Where(x => x.MentorId == null)
         .ToListAsync(CancellationToken);

      scoped[0].Mentees.Should().BeEmpty();
   }

   /// <remarks>
   ///    Two generated repositories over one connection share a query surface, so both roots resolve and the subquery
   ///    reads the table it names. Resolving every decorator against a single root — which is what the rewriter used to
   ///    do — made the inner query read the outer query's table and return the wrong rows without failing.
   /// </remarks>
   [Fact]
   public async Task Query_WithACorrelatedSubqueryAcrossTwoRepositories_ReadsTheTablesItNames()
   {
      var withAKnownAuthor = await _books.Query()
         .Where(book => _authors.Query().Any(author => author.AuthorId == book.AuthorId))
         .OrderBy(x => x.Title)
         .ToListAsync(CancellationToken);

      withAKnownAuthor.Select(x => x.Title).Should().Equal("hobbit", "narnia", "silmarillion");

      var mentoredAuthorsBooks = await _books.Query()
         .Where(book => _authors.Query().Any(author => author.AuthorId == book.AuthorId && author.MentorId != null))
         .ToListAsync(CancellationToken);

      mentoredAuthorsBooks.Select(x => x.Title).Should().Equal("narnia");
   }

   private static bool IsUntranslatable(string value)
   {
      return value.GetHashCode(StringComparison.Ordinal) > 0;
   }

   private async Task<long> CreateAuthorAsync(string name, long? mentorId)
   {
      var author = await _authors.CreateAsync(new CreateAuthorCommand { Name = name, MentorId = mentorId }, CancellationToken);

      return author.AuthorId;
   }

   private async Task<long> CreateBookAsync(string title, long? authorId, long? editorId)
   {
      var book = await _books.CreateAsync(new CreateBookCommand { Title = title, AuthorId = authorId, EditorId = editorId }, CancellationToken);

      return book.BookId;
   }

   private async Task<long> CreateTagAsync(string label)
   {
      var tag = await _tags.CreateAsync(new CreateTagCommand { Label = label }, CancellationToken);

      return tag.TagId;
   }

   private async Task CreateBookTagAsync(long bookId, long tagId)
   {
      await _bookTags.CreateAsync(new CreateBookTagCommand { BookId = bookId, TagId = tagId }, CancellationToken);
   }
}
