using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    The recommended OData configuration for a query front-end over this library's query surface. One place, so the
///    project README has one thing to point at and a change lands in one file.
/// </summary>
public static class ODataConfiguration
{
   /// <summary>The EDM namespace the fixture types are placed in, so an enum literal in a filter stays readable.</summary>
   public const string EDM_NAMESPACE = "Conformance";

   /// <summary>
   ///    Every filter function except the ones known not to reach SQL: pattern matching, which the query provider's
   ///    maintainers have declined to implement, and type checks. Excluding them turns a server-side translation
   ///    failure into a client-side validation error.
   /// </summary>
   /// <remarks>
   ///    The min/max-datetime functions cannot be excluded here, which is a gap rather than a decision: this version of
   ///    OData has dropped their <see cref="AllowedFunctions" /> members, so there is no bit to clear and validation lets
   ///    them through — OData's own expression binder then throws. <c>BlockedFunctionConformanceTests</c> pins that, and
   ///    an endpoint that cares has to reject them itself.
   /// </remarks>
   public const AllowedFunctions SUPPORTED_FUNCTIONS =
      AllowedFunctions.AllFunctions
      & ~AllowedFunctions.MatchesPattern
      & ~AllowedFunctions.IsOf;

   /// <summary>The model the conformance entity is queried through.</summary>
   public static IEdmModel Model { get; } = BuildModel(builder => builder.EntitySet<SampleData>("Samples").EntityType.HasKey(x => x.SampleId));

   /// <summary>
   ///    The settings a consumer must use. Null-propagation handling is off, and that is not a tuning choice: OData
   ///    picks the default by matching the query provider's namespace against a hardcoded list of Microsoft providers,
   ///    which this library's provider is not on and cannot join from our side. Left on, OData guards every property
   ///    access, which breaks <c>substring</c> outright, makes collection <c>all()</c> return the wrong rows, and
   ///    renders every predicate non-sargable.
   /// </summary>
   public static ODataQuerySettings QuerySettings => new() { HandleNullPropagation = HandleNullPropagationOption.False };

   /// <summary>
   ///    What a consumer gets by leaving the settings alone. Only the misconfiguration regression test uses this.
   /// </summary>
   /// <remarks>
   ///    Deliberately untouched rather than pinned to <see cref="HandleNullPropagationOption.True" />. The point of the
   ///    regression test is that the default — <see cref="HandleNullPropagationOption.Default" /> — is resolved by OData
   ///    at apply time by namespace-matching the query provider, so leaving it alone is what proves what that resolution
   ///    decides for this provider. Pinning <c>True</c> would bypass the matching and keep passing even if the provider
   ///    were one day added to the allowlist, at which point the mandate above would silently stop being true.
   /// </remarks>
   public static ODataQuerySettings MisconfiguredQuerySettings => new();

   /// <summary>
   ///    The validation a consumer must apply. In-process this is an explicit step; a hosted endpoint's
   ///    <c>[EnableQuery]</c> attribute does it for you, so copying the query settings without these gives a working
   ///    endpoint with a worse error contract.
   /// </summary>
   public static ODataValidationSettings ValidationSettings => new() { AllowedFunctions = SUPPORTED_FUNCTIONS };

   /// <summary>
   ///    Turns on the query options this suite covers. Every one of them is off by default, and <c>$top</c> is capped at
   ///    zero, so a query context that is left alone rejects every query string with a validation error. A hosted
   ///    endpoint does this through <c>AddOData(options =&gt; options.Filter().OrderBy()…)</c>; stand-alone, the query
   ///    context carries the same configuration object.
   /// </summary>
   /// <param name="configurations">The configuration object to turn the options on in.</param>
   public static void EnableSupportedQueryOptions(DefaultQueryConfigurations configurations)
   {
      ArgumentNullException.ThrowIfNull(configurations);

      configurations.EnableFilter = true;
      configurations.EnableOrderBy = true;
      configurations.EnableCount = true;
      configurations.EnableSelect = true;

      // Out of scope: $expand needs a relation model the library does not have, and $skiptoken is not covered.
      configurations.EnableExpand = false;
      configurations.EnableSkipToken = false;

      // Unbounded so the suite can page freely. A real endpoint should cap this — the query surface applies no limits
      // of its own.
      configurations.MaxTop = null;
   }

   /// <summary>
   ///    Builds the model for the awkward-types entity. Separate from <see cref="Model" /> because it is what the
   ///    characterization tests are asking about, and it is allowed to fail.
   /// </summary>
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
