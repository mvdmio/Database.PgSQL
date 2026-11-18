using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

public static class ODataConfiguration
{
   public const string EDM_NAMESPACE = "Conformance";

   public const AllowedFunctions SUPPORTED_FUNCTIONS =
      AllowedFunctions.AllFunctions
      & ~AllowedFunctions.MatchesPattern
      & ~AllowedFunctions.IsOf;

   public const int MAX_EXPANSION_DEPTH = 2;

   public static IEdmModel Model { get; } = BuildModel(builder => builder.EntitySet<SampleData>("Samples").EntityType.HasKey(x => x.SampleId));

   public static IEdmModel RelationModel { get; } = BuildModel(
      builder =>
      {
         builder.EntitySet<AuthorData>("Authors").EntityType.HasKey(x => x.AuthorId);
         builder.EntitySet<BookData>("Books").EntityType.HasKey(x => x.BookId);
      }
   );

   public static IEdmModel CompositeModel { get; } = BuildModel(
      builder =>
      {
         builder.EntitySet<TenantProjectData>("TenantProjects").EntityType.HasKey(x => x.AccountId).HasKey(x => x.ProjectId);
         builder.EntitySet<TenantTaskData>("TenantTasks").EntityType.HasKey(x => x.AccountId).HasKey(x => x.TaskId);
      }
   );

   public static ODataQuerySettings QuerySettings => new() { HandleNullPropagation = HandleNullPropagationOption.False };

   public static ODataQuerySettings MisconfiguredQuerySettings => new();

   public static ODataValidationSettings ValidationSettings => new()
   {
      AllowedFunctions = SUPPORTED_FUNCTIONS,
      MaxExpansionDepth = MAX_EXPANSION_DEPTH
   };

   public static void EnableSupportedQueryOptions(DefaultQueryConfigurations configurations)
   {
      ArgumentNullException.ThrowIfNull(configurations);

      configurations.EnableFilter = true;
      configurations.EnableOrderBy = true;
      configurations.EnableCount = true;
      configurations.EnableSelect = true;
      configurations.EnableExpand = true;

      configurations.EnableSkipToken = true;

      configurations.MaxTop = null;
   }

   public static IEdmModel BuildAwkwardModel()
   {
      return BuildModel(builder => builder.EntitySet<AwkwardData>("Awkward").EntityType.HasKey(x => x.AwkwardId));
   }

   private static IEdmModel BuildModel(Action<ODataConventionModelBuilder> configure)
   {
      var builder = new ODataConventionModelBuilder
      {
         Namespace = EDM_NAMESPACE,
         ContainerName = "Container"
      };

      configure.Invoke(builder);

      return builder.GetEdmModel();
   }
}
