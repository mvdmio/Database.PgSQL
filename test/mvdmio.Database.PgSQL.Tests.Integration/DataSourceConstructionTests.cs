using AwesomeAssertions;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Integration;

public class DataSourceConstructionTests
{
   private readonly TestFixture _fixture;

   public DataSourceConstructionTests(TestFixture fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

   [Fact]
   public async Task DirectlyConstructedConnection_BindsANonStringValueToAJsonParameter()
   {
      await using var db = new DatabaseConnection(_fixture.DbContainer.GetConnectionString());

      (await ReadTierAsync(db)).Should().Be("gold");
   }

   [Fact]
   public async Task ConnectionFromTheFactory_BindsANonStringValueToAJsonParameter()
   {
      await using var factory = new DatabaseConnectionFactory();
      var db = factory.BuildConnection(_fixture.DbContainer.GetConnectionString());

      (await ReadTierAsync(db)).Should().Be("gold");
   }

   private static async Task<string> ReadTierAsync(DatabaseConnection db)
   {
      return await db.Dapper.QuerySingleAsync<string>(
         "SELECT (:metadata) ->> 'tier'",
         new Dictionary<string, object?>
         {
            ["metadata"] = new TypedQueryParameter(new Dictionary<string, string> { ["tier"] = "gold" }, NpgsqlDbType.Jsonb)
         },
         ct: CancellationToken
      );
   }
}
