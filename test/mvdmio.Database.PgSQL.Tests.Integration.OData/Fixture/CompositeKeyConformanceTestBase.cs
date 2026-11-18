namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

public abstract class CompositeKeyConformanceTestBase : ODataTestBase
{
   protected const long FIRST_ACCOUNT = 1;

   protected const long SECOND_ACCOUNT = 2;

   private static readonly (long AccountId, string Code, string Name)[] _projects = [
      (FIRST_ACCOUNT, "apollo", "Apollo"),
      (FIRST_ACCOUNT, "borealis", "Borealis"),
      (SECOND_ACCOUNT, "aurora", "Aurora"),
      (SECOND_ACCOUNT, "vega", "Vega")
   ];

   private static readonly (long AccountId, long TaskId, string ProjectCode, string Title)[] _tasks = [
      (FIRST_ACCOUNT, 10, "apollo", "assemble"),
      (FIRST_ACCOUNT, 11, "apollo", "launch"),
      (FIRST_ACCOUNT, 12, "borealis", "survey"),
      (SECOND_ACCOUNT, 10, "aurora", "observe")
   ];

   protected TenantProjectRepository Projects { get; private set; } = null!;
   protected TenantTaskRepository Tasks { get; private set; } = null!;

   protected IReadOnlyDictionary<string, long> ProjectIds => _projectIds;

   private readonly Dictionary<string, long> _projectIds = new(StringComparer.Ordinal);

   protected CompositeKeyConformanceTestBase(ODataTestFixture fixture)
      : base(fixture)
   {
   }

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();

      Projects = new TenantProjectRepository(Db);
      Tasks = new TenantTaskRepository(Db);

      foreach (var (accountId, code, name) in _projects)
      {
         var project = await Projects.CreateAsync(
            new CreateTenantProjectCommand { AccountId = accountId, Code = code, Name = name },
            CancellationToken
         );

         _projectIds[code] = project.ProjectId;
      }

      foreach (var (accountId, taskId, projectCode, title) in _tasks)
      {
         await Tasks.CreateAsync(
            new CreateTenantTaskCommand
            {
               AccountId = accountId,
               TaskId = taskId,
               ProjectId = _projectIds[projectCode],
               Title = title
            },
            CancellationToken
         );
      }
   }

   protected AppliedQuery ApplyToProjects(string queryString)
   {
      return ODataQuery.Apply(Projects.Query(), queryString, ODataConfiguration.CompositeModel);
   }

   protected AppliedQuery ApplyToTasks(string queryString)
   {
      return ODataQuery.Apply(Tasks.Query(), queryString, ODataConfiguration.CompositeModel);
   }

   protected static async Task<IReadOnlyList<string>> CodesAsync(AppliedQuery applied)
   {
      var rows = await applied.RowsAsync<TenantProjectData>(CancellationToken);

      return rows.Select(x => x.Code).ToList();
   }

   protected static async Task<IReadOnlyList<string>> TitlesAsync(AppliedQuery applied)
   {
      var rows = await applied.RowsAsync<TenantTaskData>(CancellationToken);

      return rows.Select(x => x.Title).ToList();
   }
}
