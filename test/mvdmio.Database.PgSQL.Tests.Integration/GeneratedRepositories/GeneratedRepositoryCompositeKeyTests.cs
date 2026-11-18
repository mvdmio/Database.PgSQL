using AwesomeAssertions;
using mvdmio.Database.PgSQL.Connectors.Linq;
using mvdmio.Database.PgSQL.Exceptions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration.GeneratedRepositories;

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

      _apolloId = await CreateProjectAsync(FIRST_ACCOUNT, "apollo", "Apollo", primaryTaskId: 10);
      _borealisId = await CreateProjectAsync(FIRST_ACCOUNT, "borealis", "Borealis");
      _otherAccountsProjectId = await CreateProjectAsync(SECOND_ACCOUNT, "aurora", "Aurora", primaryTaskId: 10);

      await CreateTaskAsync(FIRST_ACCOUNT, taskId: 10, _apolloId, "assemble");
      await CreateTaskAsync(FIRST_ACCOUNT, taskId: 11, _apolloId, "launch");
      await CreateTaskAsync(FIRST_ACCOUNT, taskId: 12, _borealisId, "survey");

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

      created.AccountId.Should().Be(FIRST_ACCOUNT);
      created.ProjectId.Should().BeGreaterThan(0);

      var found = await _projects.GetByPrimaryKeyAsync(created.AccountId, created.ProjectId, CancellationToken);

      found.Should().NotBeNull();
      found!.Name.Should().Be("Cygnus");

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
      var projectLinks = await _links.Query()
         .Include(x => x.Project)
         .Where(x => x.AccountId == FIRST_ACCOUNT)
         .OrderBy(x => x.LinkId)
         .ToListAsync(CancellationToken);

      projectLinks[0].Project!.Code.Should().Be("apollo");

      projectLinks[1].Project.Should().BeNull();

      var reachedFromTheProject = await _projects.Query()
         .Include(x => x.Links)
         .Where(x => x.Code == "apollo")
         .SingleAsync(CancellationToken);

      reachedFromTheProject.Links.Select(x => x.LinkId).Should().Equal(100);
   }

   [Theory]
   [InlineData("project_id", "project_id")]
   [InlineData("project_ref", "project_id")]
   public void Query_ReachingACompositeRelation_ConstrainsEveryKeyColumnWithPlainEquality(string foreignKeyColumn, string keyColumn)
   {
      var sql = foreignKeyColumn == keyColumn
         ? QueryDiagnostics.RenderSql(_tasks.Query().Where(x => x.Project!.Name == "Apollo"))
         : QueryDiagnostics.RenderSql(_links.Query().Where(x => x.Project!.Name == "Apollo"));

      sql.Should().Contain("INNER JOIN");
      sql.Should().MatchRegex(SqlShape.CrossTableEquality("account_id", "account_id"));
      sql.Should().MatchRegex(SqlShape.CrossTableEquality(foreignKeyColumn, keyColumn));
      sql.Should().NotContain("IS NULL", "a widened join condition is what costs the second key column its index");
   }

   [Fact]
   public void Query_ReachingACompositeRelationWithoutFilteringIt_RendersAnOuterJoin()
   {
      var sql = QueryDiagnostics.RenderSql(_links.Query().Where(x => x.AccountId == FIRST_ACCOUNT).Select(x => x.Project!.Name));

      sql.Should().Contain("LEFT JOIN");
      sql.Should().MatchRegex(SqlShape.CrossTableEquality("account_id", "account_id"));
      sql.Should().MatchRegex(SqlShape.CrossTableEquality("project_ref", "project_id"));
   }

   [Fact]
   public void Query_WithInequalityOnAStringKeyMember_RendersNoNullAlternative()
   {
      var sql = QueryDiagnostics.RenderSql(_links.Query().Where(x => x.Kind != "user" && x.Project!.Name == "Apollo"));

      sql.Should().MatchRegex($"{SqlShape.QualifiedColumn("kind")}\\s*<>");
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
