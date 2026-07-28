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

   /// <summary>
   ///    The deepest expansion a client may ask for. Stated explicitly rather than left at OData's default, because the
   ///    reason it matters here is invisible: ADR 0005 records that relations are one-directional and never paired, so a
   ///    child-to-parent relation alongside a parent-to-children relation already forms a cycle — every consumer's EDM
   ///    model contains cycles by construction, and expansion depth is the only thing that bounds a client walking one
   ///    until the database gives up. It admits the deepest construct this suite covers, a two-level nested expansion,
   ///    and rejects anything past it as a validation error rather than a translation failure.
   /// </summary>
   public const int MAX_EXPANSION_DEPTH = 2;

   /// <summary>The model the conformance entity is queried through.</summary>
   public static IEdmModel Model { get; } = BuildModel(builder => builder.EntitySet<SampleData>("Samples").EntityType.HasKey(x => x.SampleId));

   /// <summary>
   ///    The model the relation-bearing pair is queried through. Separate from <see cref="Model" /> so that a navigable
   ///    member cannot change what the results already pinned against the conformance entity see: convention-based model
   ///    building would discover a relation property, pull its target type in, and widen every <c>$select</c> and
   ///    <c>$apply</c> result there.
   /// </summary>
   /// <remarks>
   ///    Both keys are declared explicitly. Convention-based key discovery looks for <c>Id</c> or a name derived from the
   ///    type's own name, and a table definition's key is neither. Both types get an entity set, because a consumer
   ///    exposing an expandable type routes to it as well.
   /// </remarks>
   public static IEdmModel RelationModel { get; } = BuildModel(
      builder =>
      {
         builder.EntitySet<AuthorData>("Authors").EntityType.HasKey(x => x.AuthorId);
         builder.EntitySet<BookData>("Books").EntityType.HasKey(x => x.BookId);
      }
   );

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
   public static ODataValidationSettings ValidationSettings => new()
   {
      AllowedFunctions = SUPPORTED_FUNCTIONS,
      MaxExpansionDepth = MAX_EXPANSION_DEPTH
   };

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
      configurations.EnableExpand = true;

      // Out of scope: $skiptoken is not covered.
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
