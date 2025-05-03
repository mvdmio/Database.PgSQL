namespace mvdmio.Database.PgSQL.Tests.Unit;

public class VerifyConfiguration
{
   [Fact]
   public void CheckVerifyConfiguration()
   {
      VerifyChecks.Run();
   }
}