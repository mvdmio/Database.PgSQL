using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

/// <summary>
///    Drives OData in-process: parses a query string into query options and applies them to a queryable, which is what
///    a hosted endpoint's <c>[EnableQuery]</c> attribute does for you.
/// </summary>
/// <remarks>
///    No web host, no controllers, no HTTP. What is under test is expression translation, and OData supports
///    constructing query options over a stand-alone request for exactly this purpose.
/// </remarks>
public static class ODataQuery
{
   /// <summary>Applies a query string to a queryable using the settings a consumer must use.</summary>
   public static AppliedQuery Apply<TEntity>(IQueryable<TEntity> query, string queryString, IEdmModel? model = null)
   {
      return Apply(query, queryString, ODataConfiguration.QuerySettings, model);
   }

   /// <summary>Applies a query string to a queryable using the given settings.</summary>
   /// <param name="query">The queryable to compose the query options over.</param>
   /// <param name="queryString">The query string a client would have sent.</param>
   /// <param name="settings">The apply-time settings.</param>
   /// <param name="model">The EDM model to parse against. Defaults to the conformance model.</param>
   public static AppliedQuery Apply<TEntity>(IQueryable<TEntity> query, string queryString, ODataQuerySettings settings, IEdmModel? model = null)
   {
      ArgumentNullException.ThrowIfNull(query);
      ArgumentNullException.ThrowIfNull(settings);

      var request = CreateRequest(queryString);
      var options = Parse<TEntity>(request, model);
      var applied = options.ApplyTo(query, settings);

      return new AppliedQuery(applied, request.ODataFeature().TotalCount);
   }

   /// <summary>
   ///    Runs the validation step a hosted endpoint performs automatically. Throws when the query string uses something
   ///    the configuration does not allow.
   /// </summary>
   /// <param name="queryString">The query string a client would have sent.</param>
   /// <param name="model">The EDM model to parse against. Defaults to the conformance model.</param>
   public static void Validate<TEntity>(string queryString, IEdmModel? model = null)
   {
      Parse<TEntity>(CreateRequest(queryString), model).Validate(ODataConfiguration.ValidationSettings);
   }

   private static ODataQueryOptions<TEntity> Parse<TEntity>(HttpRequest request, IEdmModel? model)
   {
      var context = new ODataQueryContext(model ?? ODataConfiguration.Model, typeof(TEntity), path: null);

      ODataConfiguration.EnableSupportedQueryOptions(context.DefaultQueryConfigurations);

      return new ODataQueryOptions<TEntity>(context, request);
   }

   private static HttpRequest CreateRequest(string queryString)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(queryString);

      var httpContext = new DefaultHttpContext();
      var request = httpContext.Request;

      request.Method = HttpMethods.Get;
      request.Scheme = "http";
      request.Host = new HostString("localhost");
      request.Path = "/";
      request.QueryString = new QueryString(queryString.StartsWith('?') ? queryString : $"?{queryString}");

      return request;
   }
}
