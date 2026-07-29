using AwesomeAssertions;
using mvdmio.Database.PgSQL.Dapper.QueryParameters;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;
using NpgsqlTypes;

namespace mvdmio.Database.PgSQL.Tests.Integration;

/// <summary>
///    That the two ways of getting a <see cref="DatabaseConnection" /> agree about Npgsql's dynamic JSON setting, so which
///    constructor a consumer reached for stops changing what a JSON parameter may hold.
/// </summary>
/// <remarks>
///    Not derived from <c>TestBase</c>: the point is the connection this test builds itself, rather than the one the base
///    class builds through the factory. Nothing here writes, so there is no transaction to roll back — the assertion is
///    about what the driver accepts, and a <c>SELECT</c> over a parameter asks that without needing a table.
///    <para>
///       Dynamic JSON is what lets a CLR type that is not a string be written to a <c>json</c> or <c>jsonb</c> parameter.
///       A generated repository binds a claimed <c>Dictionary</c> column exactly that way, so a consumer holding a directly
///       constructed connection used to get a driver refusal where the same code through the factory worked.
///    </para>
/// </remarks>
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
