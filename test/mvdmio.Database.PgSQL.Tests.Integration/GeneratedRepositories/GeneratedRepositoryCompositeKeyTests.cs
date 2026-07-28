using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using System.Text.RegularExpressions;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

/// <summary>
///    Covers a composite primary key end to end: the generated lookup, delete and update on the Dapper surface, and
///    filtering, ordering and eager loading across a relation whose foreign key is more than one column.
/// </summary>
/// <remarks>
///    Two accounts, each with a project and a task whose identifiers collide across them, so every assertion here
///    discriminates between "found the row" and "found a row with the same second key member".
/// </remarks>
public class GeneratedRepositoryCompositeKeyTests : TestBase
{
   private const long FIRST_ACCOUNT = 1;
   private const long SECOND_ACCOUNT = 2;

   private readonly TestFixture _fixture;

   private TenantProjectRepository _projects = null!;
   private TenantTaskRepository _tasks = null!;
   private TenantLinkRepository _links = null!;

   private long _apolloId;
   private long _borealisId;
   private long _otherAccountsProjectId;

   public GeneratedRepositoryCompositeKeyTests(TestFixture fixture)
      : base(fixture)
   {
      _fixture = fixture;
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      _projects = new TenantProjectRepository(Db);
      _tasks = new TenantTaskRepository(Db);
      _links = new TenantLinkRepository(Db);

      // Both projects with a primary task point at task 10, so only the account column decides which row that is.
      _apolloId = await CreateProjectAsync(FIRST_ACCOUNT, "apollo", "Apollo", primaryTaskId: 10);
      _borealisId = await CreateProjectAsync(FIRST_ACCOUNT, "borealis", "Borealis");
      _otherAccountsProjectId = await CreateProjectAsync(SECOND_ACCOUNT, "aurora", "Aurora", primaryTaskId: 10);

      await CreateTaskAsync(FIRST_ACCOUNT, taskId: 10, _apolloId, "assemble");
      await CreateTaskAsync(FIRST_ACCOUNT, taskId: 11, _apolloId, "launch");
      await CreateTaskAsync(FIRST_ACCOUNT, taskId: 12, _borealisId, "survey");

      // The same task identifier under another account, which is the whole reason the key is composite.
      await CreateTaskAsync(SECOND_ACCOUNT, taskId: 10, _otherAccountsProjectId, "observe");

      await CreateLinkAsync(FIRST_ACCOUNT, linkId: 100, kind: "project", targetId: _apolloId);
      await CreateLinkAsync(FIRST_ACCOUNT, linkId: 101, kind: "user", targetId: 9999);
   }

   [Fact]
   public async Task CrudOperations_OverACompositeKey_WorkEndToEnd()
   {
      var created = await _projects.CreateAsync(
         new CreateTenantProjectCommand { AccountId = FIRST_ACCOUNT, Code = "cygnus", Name = "Cygnus" },
         CancellationToken
      );

      // The second key member is database-generated, so it comes back rather than being supplied.
      created.AccountId.Should().Be(FIRST_ACCOUNT);
      created.ProjectId.Should().BeGreaterThan(0);

      var found = await _projects.GetByPrimaryKeyAsync(created.AccountId, created.ProjectId, CancellationToken);

      found.Should().NotBeNull();
      found!.Name.Should().Be("Cygnus");

      // A [Unique] column keeps its own lookup, named after the property, so two unique lookups stay distinguishable.
      (await _projects.GetByCodeAsync("cygnus", CancellationToken))!.ProjectId.Should().Be(created.ProjectId);

      var updated = await _projects.UpdateAsync(
         new UpdateTenantProjectCommand
         {
            AccountId = created.AccountId,
            ProjectId = created.ProjectId,
            Code = "cygnus",
            Name = "Cygnus renamed"
         },
         CancellationToken
      );

      updated.Name.Should().Be("Cygnus renamed");

      (await _projects.DeleteByPrimaryKeyAsync(created.AccountId, created.ProjectId, CancellationToken)).Should().BeTrue();
      (await _projects.GetByPrimaryKeyAsync(created.AccountId, created.ProjectId, CancellationToken)).Should().BeNull();
   }

   [Fact]
   public async Task GetByPrimaryKeyAsync_WithOneKeyMemberFromAnotherRow_FindsNothing()
   {
      // Both parameters are needed to address a row, which is the structural guarantee a composite key buys.
      (await _tasks.GetByPrimaryKeyAsync(FIRST_ACCOUNT, 10, CancellationToken))!.Title.Should().Be("assemble");
      (await _tasks.GetByPrimaryKeyAsync(SECOND_ACCOUNT, 10, CancellationToken))!.Title.Should().Be("observe");
      (await _tasks.GetByPrimaryKeyAsync(SECOND_ACCOUNT, 11, CancellationToken)).Should().BeNull();
   }

   [Fact]
   public async Task UpdateAsync_OverACompositeKey_AffectsExactlyOneRow()
   {
      await _tasks.UpdateAsync(
         new UpdateTenantTaskCommand
         {
            AccountId = FIRST_ACCOUNT,
            TaskId = 10,
            ProjectId = _apolloId,
            Title = "assemble again"
         },
         CancellationToken
      );

      (await _tasks.GetByPrimaryKeyAsync(FIRST_ACCOUNT, 10, CancellationToken))!.Title.Should().Be("assemble again");

      // The row sharing the task identifier under the other account is untouched.
      (await _tasks.GetByPrimaryKeyAsync(SECOND_ACCOUNT, 10, CancellationToken))!.Title.Should().Be("observe");
   }

   [Fact]
   public async Task DeleteByPrimaryKeyAsync_OverACompositeKey_RemovesExactlyOneRow()
   {
      (await _tasks.DeleteByPrimaryKeyAsync(FIRST_ACCOUNT, 10, CancellationToken)).Should().BeTrue();

      (await _tasks.GetByPrimaryKeyAsync(FIRST_ACCOUNT, 10, CancellationToken)).Should().BeNull();
      (await _tasks.GetByPrimaryKeyAsync(SECOND_ACCOUNT, 10, CancellationToken)).Should().NotBeNull();
      (await _tasks.DeleteByPrimaryKeyAsync(FIRST_ACCOUNT, 10, CancellationToken)).Should().BeFalse();
   }

   [Fact]
   public async Task CreateAsync_OverAFourColumnKeyWithAGeneratedMember_ReadsTheComputedValueBack()
   {
      var link = await _links.GetByPrimaryKeyAsync(FIRST_ACCOUNT, 100, "project", 1, CancellationToken);

      link.Should().NotBeNull();
      link!.ProjectRef.Should().Be(_apolloId);

      // The per-kind column is null for every other kind, which is what makes the junction polymorphic.
      (await _links.GetByPrimaryKeyAsync(FIRST_ACCOUNT, 101, "user", 1, CancellationToken))!.ProjectRef.Should().BeNull();
   }

   [Fact]
   public async Task Query_FilteringAndOrderingAcrossACompositeRelationToOneRow_NeedsNoExtraApi()
   {
      var apollosTasks = await _tasks.Query()
         .Where(x => x.Project!.Name == "Apollo")
         .OrderBy(x => x.Title)
         .ToListAsync(CancellationToken);

      apollosTasks.Select(x => x.Title).Should().Equal("assemble", "launch");

      var byProjectName = await _tasks.Query()
         .Where(x => x.AccountId == FIRST_ACCOUNT)
         .OrderByDescending(x => x.Project!.Name)
         .ThenBy(x => x.Title)
         .ToListAsync(CancellationToken);

      byProjectName.Select(x => x.Title).Should().Equal("survey", "assemble", "launch");
   }

   /// <remarks>
   ///    The tenancy guarantee in query form, and the assertion that would fail if either key column were dropped from the
   ///    join. Both accounts have a task numbered 10, so the second key member alone identifies two rows and only the
   ///    account column tells them apart.
   /// </remarks>
   [Fact]
   public async Task Query_AcrossACompositeRelation_ReachesTheRowInsideTheSameAccount()
   {
      var materialized = await _projects.Query()
         .Include(x => x.PrimaryTask)
         .Where(x => x.PrimaryTaskId != null)
         .OrderBy(x => x.Code)
         .ToListAsync(CancellationToken);

      materialized.Select(x => x.Code).Should().Equal("apollo", "aurora");
      materialized[0].PrimaryTask!.Title.Should().Be("assemble");
      materialized[1].PrimaryTask!.Title.Should().Be("observe");

      var byTaskTitle = await _projects.Query()
         .Where(x => x.PrimaryTask!.Title == "observe")
         .ToListAsync(CancellationToken);

      byTaskTitle.Select(x => x.Code).Should().Equal("aurora");
   }

   [Fact]
   public async Task Query_FilteringAcrossACompositeRelationToManyRows_ReturnsTheMatchingRows()
   {
      var withALaunch = await _projects.Query()
         .Where(x => x.Tasks.Any(task => task.Title == "launch"))
         .ToListAsync(CancellationToken);

      withALaunch.Select(x => x.Code).Should().Equal("apollo");

      var withoutTasks = await _projects.Query()
         .Where(x => !x.Tasks.Any())
         .ToListAsync(CancellationToken);

      withoutTasks.Should().BeEmpty();
   }

   [Fact]
   public async Task Query_MaterializingACompositeRelationToOneRow_PopulatesTheMirroredProperty()
   {
      var tasks = await _tasks.Query()
         .Include(x => x.Project)
         .Where(x => x.AccountId == FIRST_ACCOUNT)
         .OrderBy(x => x.TaskId)
         .ToListAsync(CancellationToken);

      tasks.Select(x => x.Project!.Code).Should().Equal("apollo", "apollo", "borealis");
   }

   [Fact]
   public async Task Query_MaterializingACompositeRelationToManyRows_PopulatesTheCollection()
   {
      var projects = await _projects.Query()
         .Include(x => x.Tasks)
         .OrderBy(x => x.Code)
         .ToListAsync(CancellationToken);

      projects.Select(x => x.Code).Should().Equal("apollo", "aurora", "borealis");
      projects[0].Tasks.Select(x => x.Title).Should().BeEquivalentTo("assemble", "launch");
      projects[1].Tasks.Select(x => x.Title).Should().Equal("observe");
   }

   [Fact]
   public async Task Query_ChainingMaterializationThroughACompositeRelation_PopulatesBothLevels()
   {
      var apollo = await _projects.Query()
         .Include(x => x.Tasks)
         .ThenInclude(x => x.Project)
         .Where(x => x.Code == "apollo")
         .SingleAsync(CancellationToken);

      apollo.Tasks.Should().HaveCount(2);
      apollo.Tasks.Should().AllSatisfy(task => task.Project!.Code.Should().Be("apollo"));
   }

   [Fact]
   public async Task Query_WithAFilteredMaterializationOverACompositeRelation_LoadsOnlyTheScopedRows()
   {
      var apollo = await _projects.Query()
         .Include(x => x.Tasks, tasks => tasks.Where(task => task.Title == "launch"))
         .Where(x => x.Code == "apollo")
         .SingleAsync(CancellationToken);

      apollo.Tasks.Select(x => x.Title).Should().Equal("launch");
   }

   [Fact]
   public async Task Query_AcrossACompositeRelationOnAStoredGeneratedColumn_ReachesTheRelatedRow()
   {
      // A generated column is an ordinary mapped column from the query surface's side, so a relation over it is ordinary.
      var projectLinks = await _links.Query()
         .Include(x => x.Project)
         .Where(x => x.AccountId == FIRST_ACCOUNT)
         .OrderBy(x => x.LinkId)
         .ToListAsync(CancellationToken);

      projectLinks[0].Project!.Code.Should().Be("apollo");

      // Null for every other kind, and the row itself is still returned because a relation is an outer join.
      projectLinks[1].Project.Should().BeNull();

      var reachedFromTheProject = await _projects.Query()
         .Include(x => x.Links)
         .Where(x => x.Code == "apollo")
         .SingleAsync(CancellationToken);

      reachedFromTheProject.Links.Select(x => x.LinkId).Should().Equal(100);
   }

   /// <remarks>
   ///    Regression cover for a measured plan cliff. A nullable key member on both sides of a join makes the provider widen
   ///    the condition with an "or both are null" alternative, which demotes the second column out of a composite index's
   ///    condition into a filter. Refusing a nullable key member makes that unreachable, and this is what says so: every
   ///    key column is a plain cross-table equality, including against the nullable generated column.
   /// </remarks>
   [Theory]
   [InlineData("project_id", "project_id")]
   [InlineData("project_ref", "project_id")]
   public void Query_ReachingACompositeRelation_ConstrainsEveryKeyColumnWithPlainEquality(string foreignKeyColumn, string keyColumn)
   {
      // The second case is the nullable foreign key against a non-nullable key member — the generated-column shape — and
      // it renders the same plain equality as the first.
      var sql = foreignKeyColumn == keyColumn
         ? RenderSql(_tasks.Query().Where(x => x.Project!.Name == "Apollo"))
         : RenderSql(_links.Query().Where(x => x.Project!.Name == "Apollo"));

      // An inner join, even though the relation is an outer one by contract: the filter is an equality on a column that
      // cannot hold null, so no null-extended row could satisfy it and the provider collapses the join. That is a plan
      // improvement rather than a change of meaning — the outer-join behaviour itself is pinned by
      // Query_AcrossACompositeRelationOnAStoredGeneratedColumn_ReachesTheRelatedRow, which filters nothing.
      sql.Should().Contain("INNER JOIN");
      sql.Should().MatchRegex(CrossTableEquality("account_id", "account_id"));
      sql.Should().MatchRegex(CrossTableEquality(foreignKeyColumn, keyColumn));
      sql.Should().NotContain("IS NULL", "a widened join condition is what costs the second key column its index");
   }

   /// <remarks>
   ///    The join a relation renders when nothing filters the far side, which is what the test above collapses to an
   ///    inner join and therefore stops pinning. A relation is an outer join by contract — a foreign key pointing at a
   ///    missing row yields nothing rather than dropping the row that holds it.
   /// </remarks>
   [Fact]
   public void Query_ReachingACompositeRelationWithoutFilteringIt_RendersAnOuterJoin()
   {
      var sql = RenderSql(_links.Query().Where(x => x.AccountId == FIRST_ACCOUNT).Select(x => x.Project!.Name));

      sql.Should().Contain("LEFT JOIN");
      sql.Should().MatchRegex(CrossTableEquality("account_id", "account_id"));
      sql.Should().MatchRegex(CrossTableEquality("project_ref", "project_id"));
   }

   /// <remarks>
   ///    The same guarantee for a key member typed non-nullable <c>string</c>, which is the shape a pure CLR-type test
   ///    reads as nullable — <c>string</c> and <c>string?</c> are one type to it. Inequality is what shows the
   ///    difference, because equality excludes nulls on its own and so is never widened. The predicate is on the driving
   ///    table's own column deliberately: one reaching across the relation stays widened whatever the column says,
   ///    because the relation is an outer join and its whole table is the nullable side.
   /// </remarks>
   [Fact]
   public void Query_WithInequalityOnAStringKeyMember_RendersNoNullAlternative()
   {
      var sql = RenderSql(_links.Query().Where(x => x.Kind != "user" && x.Project!.Name == "Apollo"));

      sql.Should().MatchRegex($"{QualifiedColumn("kind")}\\s*<>");
      sql.Should().NotContain("IS NULL", "the null alternative can never match a column that cannot hold null");
   }

   [Fact]
   public async Task Query_WithMaterializationOfACompositeRelationComposedBeforeATransactionBegins_RunsInsideThatTransaction()
   {
      await using var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var projects = new TenantProjectRepository(connection);
      var tasks = new TenantTaskRepository(connection);

      var query = projects.Query().Include(x => x.Tasks).Where(x => x.Code == "uncommitted");

      await connection.BeginTransactionAsync(ct: CancellationToken);

      try
      {
         var project = await projects.CreateAsync(
            new CreateTenantProjectCommand { AccountId = 42, Code = "uncommitted", Name = "Uncommitted" },
            CancellationToken
         );

         await tasks.CreateAsync(
            new CreateTenantTaskCommand
            {
               AccountId = project.AccountId,
               TaskId = 1,
               ProjectId = project.ProjectId,
               Title = "uncommitted task"
            },
            CancellationToken
         );

         var rows = await query.ToListAsync(CancellationToken);

         rows.Should().ContainSingle();
         rows[0].Tasks.Select(x => x.Title).Should().Equal("uncommitted task");
      }
      finally
      {
         await connection.RollbackTransactionAsync(CancellationToken);
      }
   }

   /// <remarks>
   ///    The decorator is what carries exception translation, SQL diagnostics and the disposed-connection error, and a
   ///    composite relation goes through the same execution-time include translation as a single-column one — so this is
   ///    regression cover for the claim that nothing about it drops the decorator from the chain.
   /// </remarks>
   [Fact]
   public async Task Query_AcrossACompositeRelation_KeepsExceptionTranslationInTheChain()
   {
      var untranslatable = _projects.Query().Include(x => x.Tasks).Where(x => IsUntranslatable(x.Name));

      (await Record.ExceptionAsync(() => untranslatable.ToListAsync(CancellationToken))).Should().BeOfType<QueryTranslationException>();
      Record.Exception(() => untranslatable.ToList()).Should().BeOfType<QueryTranslationException>();

      var zero = 0;

      var databaseFailure = await Record.ExceptionAsync(
         () => _tasks.Query().Include(x => x.Project).Where(x => x.TaskId / zero == 1).ToListAsync(CancellationToken)
      );

      databaseFailure.Should().BeOfType<QueryException>();
      ((QueryException)databaseFailure!).Sql.Should().Contain("generated_tenant_tasks");
   }

   [Fact]
   public async Task Query_AcrossACompositeRelationEnumeratedAfterTheConnectionIsDisposed_ThrowsObjectDisposedException()
   {
      var connection = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());
      var query = new TenantTaskRepository(connection).Query().Include(x => x.Project);

      await connection.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() => query.ToListAsync(CancellationToken));
   }

   private static bool IsUntranslatable(string value)
   {
      return value.GetHashCode(StringComparison.Ordinal) > 0;
   }

   /// <summary>
   ///    An equality between two qualified columns, whichever table aliases the provider chose and whether or not it
   ///    quoted them.
   /// </summary>
   private static string CrossTableEquality(string foreignKeyColumn, string keyColumn)
   {
      return $"{QualifiedColumn(foreignKeyColumn)}\\s*=\\s*{QualifiedColumn(keyColumn)}";
   }

   private static string QualifiedColumn(string columnName)
   {
      return $@"(?:""[^""]+""|\w+)\.""?{Regex.Escape(columnName)}""?";
   }

   private static string RenderSql<TEntity>(IQueryable<TEntity> query)
   {
      return QueryDiagnostics.RenderSql(query);
   }

   /// <remarks>
   ///    The primary task is named at creation time even though the task does not exist yet, which the schema permits
   ///    because that column carries no foreign key — see the fixture.
   /// </remarks>
   private async Task<long> CreateProjectAsync(long accountId, string code, string name, long? primaryTaskId = null)
   {
      var project = await _projects.CreateAsync(
         new CreateTenantProjectCommand { AccountId = accountId, Code = code, Name = name, PrimaryTaskId = primaryTaskId },
         CancellationToken
      );

      return project.ProjectId;
   }

   private async Task CreateTaskAsync(long accountId, long taskId, long projectId, string title)
   {
      await _tasks.CreateAsync(
         new CreateTenantTaskCommand { AccountId = accountId, TaskId = taskId, ProjectId = projectId, Title = title },
         CancellationToken
      );
   }

   private async Task CreateLinkAsync(long accountId, long linkId, string kind, long targetId)
   {
      await _links.CreateAsync(
         new CreateTenantLinkCommand { AccountId = accountId, LinkId = linkId, Kind = kind, Ordinal = 1, TargetId = targetId },
         CancellationToken
      );
   }
}
