using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Extensions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;

namespace mvdmio.Database.PgSQL.Tests.Integration.OData.Fixture;

public static class ODataQuery
{
   public static AppliedQuery Apply<TEntity>(IQueryable<TEntity> query, string queryString, IEdmModel? model = null)
   {
      return Apply(query, queryString, ODataConfiguration.QuerySettings, model);
   }

   public static AppliedQuery Apply<TEntity>(IQueryable<TEntity> query, string queryString, ODataQuerySettings settings, IEdmModel? model = null)
   {
      ArgumentNullException.ThrowIfNull(query);
      ArgumentNullException.ThrowIfNull(settings);

      var request = CreateRequest(queryString);
      var options = Parse<TEntity>(request, model);
      var applied = options.ApplyTo(query, settings);

      return new AppliedQuery(applied, request.ODataFeature().TotalCount);
   }

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
