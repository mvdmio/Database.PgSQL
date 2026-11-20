using AwesomeAssertions;
using mvdmio.Database.PgSQL.Tests.Integration.Fixture;

namespace mvdmio.Database.PgSQL.Tests.Integration;

public class ServerVersionTests : TestBase
{
   public ServerVersionTests(TestFixture fixture)
      : base(fixture)
   {
   }

   [Fact]
   public async Task SelectVersion_OnSharedContainer_ReportsPostgres19()
   {
      var version = await Db.Dapper.ExecuteScalarAsync<string>("SELECT version()", ct: CancellationToken);

      version.Should().StartWith("PostgreSQL 19.");
   }
}
