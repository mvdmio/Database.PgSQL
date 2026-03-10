using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace mvdmio.Database.PgSQL.Tests.Unit;

public class ServiceCollectionExtensionsTests
{
   [Fact]
   public void AddDatabase_RegistersFactory()
   {
      var services = new ServiceCollection();

      services.AddDatabase();

      services.Should().ContainSingle(x => x.ServiceType == typeof(DatabaseConnectionFactory) && x.ImplementationType == typeof(DatabaseConnectionFactory));
   }

   [Fact]
   public void AddDatabase_UsesSingletonLifetime()
   {
      var services = new ServiceCollection();

      services.AddDatabase();

      services.Should().ContainSingle(x => x.ServiceType == typeof(DatabaseConnectionFactory) && x.Lifetime == ServiceLifetime.Singleton);
   }
}
