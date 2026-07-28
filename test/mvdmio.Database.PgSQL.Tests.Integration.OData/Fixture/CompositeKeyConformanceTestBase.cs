namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    Seeds the tenant project-and-task graph and applies query strings against
///    <see cref="ODataConfiguration.CompositeModel" />. Parallel to <see cref="RelationConformanceTestBase" />, and for the
///    same reason: every test goes through a generated repository's <c>Query()</c>, because that is the seam a consumer
///    calls.
/// </summary>
public abstract class CompositeKeyConformanceTestBase : ODataTestBase
{
   /// <summary>The account the majority of the fixture belongs to.</summary>
   protected const long FIRST_ACCOUNT = 1;

   /// <summary>The second tenant, whose task numbering deliberately collides with the first's.</summary>
   protected const long SECOND_ACCOUNT = 2;

   /// <summary>
   ///    Chosen so that every assertion discriminates: no two names share a first letter, both accounts have a task
   ///    numbered 10 so only the tenancy column tells them apart, and one project has no tasks so an empty expanded
   ///    collection is observable.
   /// </summary>
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

   /// <summary>The generated identifier of each seeded project, by its code.</summary>
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

   /// <summary>Applies a query string to the project repository's queryable using the recommended settings.</summary>
   protected AppliedQuery ApplyToProjects(string queryString)
   {
      return ODataQuery.Apply(Projects.Query(), queryString, ODataConfiguration.CompositeModel);
   }

   /// <summary>Applies a query string to the task repository's queryable using the recommended settings.</summary>
   protected AppliedQuery ApplyToTasks(string queryString)
   {
      return ODataQuery.Apply(Tasks.Query(), queryString, ODataConfiguration.CompositeModel);
   }

   /// <summary>The codes an applied query returned, in the order the database gave them. Codes are unique here.</summary>
   protected static async Task<IReadOnlyList<string>> CodesAsync(AppliedQuery applied)
   {
      var rows = await applied.RowsAsync<TenantProjectData>(CancellationToken);

      return rows.Select(x => x.Code).ToList();
   }

   /// <summary>The titles an applied query returned, in the order the database gave them. Titles are unique here.</summary>
   protected static async Task<IReadOnlyList<string>> TitlesAsync(AppliedQuery applied)
   {
      var rows = await applied.RowsAsync<TenantTaskData>(CancellationToken);

      return rows.Select(x => x.Title).ToList();
   }
}
