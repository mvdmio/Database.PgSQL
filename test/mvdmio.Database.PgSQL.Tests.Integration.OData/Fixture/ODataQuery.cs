using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;

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
   public static AppliedQuery Apply<TEntity>(IQueryable<TEntity> query, string queryString)
   {
      return Apply(query, queryString, ODataConfiguration.QuerySettings);
   }

   /// <summary>Applies a query string to a queryable using the given settings.</summary>
   public static AppliedQuery Apply<TEntity>(IQueryable<TEntity> query, string queryString, ODataQuerySettings settings)
   {
      ArgumentNullException.ThrowIfNull(query);
      ArgumentNullException.ThrowIfNull(settings);

      var request = CreateRequest(queryString);
      var options = Parse<TEntity>(request);
      var applied = options.ApplyTo(query, settings);

      return new AppliedQuery(applied, request.ODataFeature().TotalCount);
   }

   /// <summary>
   ///    Runs the validation step a hosted endpoint performs automatically. Throws when the query string uses something
   ///    the configuration does not allow.
   /// </summary>
   public static void Validate<TEntity>(string queryString)
   {
      Parse<TEntity>(CreateRequest(queryString)).Validate(ODataConfiguration.ValidationSettings);
   }

   private static ODataQueryOptions<TEntity> Parse<TEntity>(HttpRequest request)
   {
      var context = new ODataQueryContext(ODataConfiguration.Model, typeof(TEntity), path: null);

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
