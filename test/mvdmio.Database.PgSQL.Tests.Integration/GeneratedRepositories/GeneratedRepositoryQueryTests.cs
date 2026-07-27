using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    Covers the query surface end to end: the generator, the emitted mapping, the adapter, the decorator, the
///    query provider and PostgreSQL. <see cref="TestBase" /> opens a transaction for every test, so each of these
///    also proves the adapter binds to the ambient transaction.
/// </summary>
public class GeneratedRepositoryQueryTests : TestBase
{
   private readonly TestFixture _fixture;
   private ProfileRepository _repository = null!;

   public GeneratedRepositoryQueryTests(TestFixture fixture)
      : base(fixture)
   {
      _fixture = fixture;
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _repository = new ProfileRepository(Db);

      await CreateProfileAsync("alice", nickname: null, new DateOnly(1990, 2, 3), new TimeOnly(7, 15), new Uri("https://example.com/alice"), new Dictionary<string, string> { ["tier"] = "gold" });
      await CreateProfileAsync("bob", "bobby", new DateOnly(1985, 6, 15), new TimeOnly(8, 30), homePage: null, metadata: null);
      await CreateProfileAsync("carol", "caz", new DateOnly(2000, 12, 31), new TimeOnly(6, 0), homePage: null, metadata: null);
   }

   [Fact]
   public async Task Query_WithEqualityFilter_ReturnsOnlyMatchingRows()
   {
      var rows = await _repository.Query().Where(x => x.Handle == "bob").ToListAsync(CancellationToken);

      rows.Should().ContainSingle();
      rows[0].Nickname.Should().Be("bobby");
   }

   [Fact]
   public async Task Query_WithOrderingComparisonAndBooleanCombination_ReturnsMatchingRows()
   {
      var rows = await _repository.Query()
         .Where(x => x.BirthDate > new DateOnly(1986, 1, 1) && x.WakeTime > new TimeOnly(7, 0))
         .ToListAsync(CancellationToken);

      rows.Should().ContainSingle();
      rows[0].Handle.Should().Be("alice");

      var either = await _repository.Query()
         .Where(x => x.Handle == "bob" || x.Handle == "carol")
         .ToListAsync(CancellationToken);

      either.Select(x => x.Handle).Should().BeEquivalentTo("bob", "carol");
   }

   [Fact]
   public async Task Query_WithNotEqualsOnNullableColumn_ReturnsRowsWhereColumnIsNull()
   {
      var rows = await _repository.Query().Where(x => x.Nickname != "bobby").ToListAsync(CancellationToken);

      rows.Select(x => x.Handle).Should().BeEquivalentTo("alice", "carol");
   }

   [Fact]
   public async Task Query_WithOrderByAndThenBy_SortsInBothDirections()
   {
      var ascending = await _repository.Query()
         .OrderBy(x => x.Handle)
         .ToListAsync(CancellationToken);

      ascending.Select(x => x.Handle).Should().Equal("alice", "bob", "carol");

      var descending = await _repository.Query()
         .OrderByDescending(x => x.WakeTime)
         .ThenBy(x => x.Handle)
         .ToListAsync(CancellationToken);

      descending.Select(x => x.Handle).Should().Equal("bob", "alice", "carol");

      var thenDescending = await _repository.Query()
         .OrderBy(x => x.Nickname == null)
         .ThenByDescending(x => x.Handle)
         .ToListAsync(CancellationToken);

      thenDescending.Select(x => x.Handle).Should().Equal("carol", "bob", "alice");
   }

   [Fact]
   public async Task Query_WithSkipAndTake_PagesInTheDatabase()
   {
      var query = _repository.Query()
         .OrderBy(x => x.Handle)
         .Skip(1)
         .Take(1);

      QueryDiagnostics.RenderSql(query).Should().ContainAll("LIMIT", "OFFSET");

      var window = await query.ToListAsync(CancellationToken);

      window.Select(x => x.Handle).Should().Equal("bob");
   }

   [Fact]
   public async Task Query_WithAggregateOperators_CountsInTheDatabase()
   {
      (await _repository.Query().LongCountAsync(CancellationToken)).Should().Be(3L);
      (await _repository.Query().AnyAsync(CancellationToken)).Should().BeTrue();
      (await _repository.Query().Where(x => x.Handle == "nobody").AnyAsync(CancellationToken)).Should().BeFalse();

      _repository.Query().LongCount().Should().Be(3L);
      _repository.Query().Any().Should().BeTrue();

      var counted = _repository.Query();
      (await counted.CountAsync(CancellationToken)).Should().Be(3);
      counted.Count().Should().Be(3);

      // The count came from the database, not from a materialized sequence: the last statement sent was a COUNT that
      // never selected a column.
      var sql = QueryDiagnostics.LastSql(counted)!.ToUpperInvariant();
      sql.Should().Contain("COUNT(");
      sql.Should().NotContain("HANDLE");
   }

   [Fact]
   public async Task Query_WithSingleRowOperators_ReturnsTheExpectedRow()
   {
      (await _repository.Query().OrderBy(x => x.Handle).FirstAsync(CancellationToken)).Handle.Should().Be("alice");
      (await _repository.Query().Where(x => x.Handle == "nobody").FirstOrDefaultAsync(CancellationToken)).Should().BeNull();
      (await _repository.Query().Where(x => x.Handle == "carol").SingleAsync(CancellationToken)).Nickname.Should().Be("caz");
      (await _repository.Query().Where(x => x.Handle == "nobody").SingleOrDefaultAsync(CancellationToken)).Should().BeNull();

      _repository.Query().OrderBy(x => x.Handle).First().Handle.Should().Be("alice");
      _repository.Query().Where(x => x.Handle == "nobody").FirstOrDefault().Should().BeNull();
      _repository.Query().Where(x => x.Handle == "carol").Single().Handle.Should().Be("carol");
      _repository.Query().Where(x => x.Handle == "nobody").SingleOrDefault().Should().BeNull();
   }

   [Fact]
   public async Task Query_WithSingleRowOperators_WhenTheRowCountIsWrong_Throws()
   {
      await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.Query().Where(x => x.Handle == "nobody").FirstAsync(CancellationToken));
      await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.Query().SingleAsync(CancellationToken));

      Assert.Throws<InvalidOperationException>(() => _repository.Query().Where(x => x.Handle == "nobody").First());
      Assert.Throws<InvalidOperationException>(() => _repository.Query().Single());
   }

   [Fact]
   public void Query_WithRuntimeLocalVariable_RendersASqlParameter()
   {
      var handle = "bob";

      var sql = QueryDiagnostics.RenderSql(_repository.Query().Where(x => x.Handle == handle));

      sql.Should().Contain(":handle");
      sql.Should().NotContain("'bob'");
   }

   [Fact]
   public async Task Query_WithAnOverriddenDialect_StillTranslatesAndExecutes()
   {
      Db.Linq.Dialect = PostgresDialect.V13;

      try
      {
         var rows = await _repository.Query().OrderBy(x => x.Handle).Skip(1).Take(1).ToListAsync(CancellationToken);

         rows.Select(x => x.Handle).Should().Equal("bob");
      }
      finally
      {
         Db.Linq.Dialect = PostgresDialect.Latest;
      }
   }

   [Fact]
   public async Task Query_AfterCreatingThroughTheDapperPath_SeesTheRowInTheSameTransaction()
   {
      await CreateProfileAsync("dave", "davey", new DateOnly(1970, 1, 1), new TimeOnly(5, 0), homePage: null, metadata: null);

      var rows = await _repository.Query().Where(x => x.Handle == "dave").ToListAsync(CancellationToken);

      rows.Should().ContainSingle();
   }

   [Fact]
   public async Task Query_RoundTripsTheConvertedTypes()
   {
      var alice = await _repository.Query().Where(x => x.Handle == "alice").SingleAsync(CancellationToken);

      alice.BirthDate.Should().Be(new DateOnly(1990, 2, 3));
      alice.WakeTime.Should().Be(new TimeOnly(7, 15));
      alice.HomePage.Should().Be(new Uri("https://example.com/alice"));
      alice.Metadata.Should().BeEquivalentTo(new Dictionary<string, string> { ["tier"] = "gold" });

      var bob = await _repository.Query().Where(x => x.Handle == "bob").SingleAsync(CancellationToken);

      bob.HomePage.Should().BeNull();
      bob.Metadata.Should().BeNull();
   }

   [Fact]
   public async Task Query_FiltersOnTheConvertedTypes()
   {
      var homePage = new Uri("https://example.com/alice");

      var byUri = await _repository.Query().Where(x => x.HomePage == homePage).ToListAsync(CancellationToken);
      byUri.Select(x => x.Handle).Should().Equal("alice");

      var born = new DateOnly(1990, 1, 1);
      var afterBorn = await _repository.Query().Where(x => x.BirthDate > born).CountAsync(CancellationToken);
      afterBorn.Should().Be(2);
   }

   [Fact]
   public async Task Query_ConsumedAsAsyncEnumerable_YieldsEveryRow()
   {
      var handles = new List<string>();

      await foreach (var profile in (IAsyncEnumerable<ProfileData>)_repository.Query().OrderBy(x => x.Handle))
      {
         handles.Add(profile.Handle);
      }

      handles.Should().Equal("alice", "bob", "carol");
   }

   [Fact]
   public async Task Query_WithCommandTimeout_RunsAgainstTheSameTransaction()
   {
      await CreateProfileAsync("erin", null, new DateOnly(1995, 3, 3), new TimeOnly(9, 45), homePage: null, metadata: null);

      var withTimeout = await _repository.Query(TimeSpan.FromSeconds(30)).ToListAsync(CancellationToken);
      var withoutTimeout = await _repository.Query().ToListAsync(CancellationToken);

      withTimeout.Should().HaveCount(4);
      withTimeout.Select(x => x.Handle).Should().BeEquivalentTo(withoutTimeout.Select(x => x.Handle));
   }

   [Fact]
   public async Task Query_WithUntranslatableExpression_ThrowsQueryTranslationException()
   {
      var query = _repository.Query().Where(x => IsUntranslatable(x.Handle));

      var asyncFailure = await Record.ExceptionAsync(() => query.ToListAsync(CancellationToken));
      var syncFailure = Record.Exception(() => query.ToList());

      asyncFailure.Should().BeOfType<QueryTranslationException>();
      syncFailure.Should().BeOfType<QueryTranslationException>();
   }

   [Fact]
   public async Task Query_ThatFailsInTheDatabase_ThrowsQueryExceptionCarryingTheSql()
   {
      var zero = 0;

      var failure = await Record.ExceptionAsync(() => _repository.Query().Where(x => x.ProfileId / zero == 1).ToListAsync(CancellationToken));

      failure.Should().BeOfType<QueryException>();
      ((QueryException)failure!).Sql.Should().Contain("generated_profiles");
   }

   [Fact]
   public async Task Query_ComposedBeforeATransactionBegins_RunsAgainstTheCurrentTransaction()
   {
      await using var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var repository = new ProfileRepository(connection);

      var query = repository.Query().Where(x => x.Handle == "frank");

      await connection.BeginTransactionAsync(ct: CancellationToken);

      try
      {
         await repository.CreateAsync(
            new CreateProfileCommand
            {
               Handle = "frank",
               BirthDate = new DateOnly(1960, 4, 4),
               WakeTime = new TimeOnly(4, 30)
            },
            CancellationToken
         );

         var rows = await query.ToListAsync(CancellationToken);

         rows.Should().ContainSingle();
      }
      finally
      {
         await connection.RollbackTransactionAsync(CancellationToken);
      }
   }

   [Fact]
   public async Task Query_EnumeratedAfterTheConnectionIsDisposed_ThrowsObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var query = new ProfileRepository(connection).Query();

      await connection.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() => query.ToListAsync(CancellationToken));
   }

   private static bool IsUntranslatable(string value)
   {
      return value.GetHashCode(StringComparison.Ordinal) > 0;
   }

   private async Task CreateProfileAsync(
      string handle,
      string? nickname,
      DateOnly birthDate,
      TimeOnly wakeTime,
      Uri? homePage,
      Dictionary<string, string>? metadata
   )
   {
      await _repository.CreateAsync(
         new CreateProfileCommand
         {
            Handle = handle,
            Nickname = nickname,
            BirthDate = birthDate,
            WakeTime = wakeTime,
            HomePage = homePage,
            Metadata = metadata
         },
         CancellationToken
      );
   }
}
