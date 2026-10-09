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

      services.Should().ContainSingle(x => x.ServiceType == typeof(DatabaseConnectionFactory));
   }

   [Fact]
   public void AddDatabase_UsesSingletonLifetime()
   {
      var services = new ServiceCollection();

      services.AddDatabase();

      services.Should().ContainSingle(x => x.ServiceType == typeof(DatabaseConnectionFactory) && x.Lifetime == ServiceLifetime.Singleton);
   }

   [Fact]
   public void AddDatabase_WithNullSettings_Throws()
   {
      var services = new ServiceCollection();

      var act = () => services.AddDatabase(null!);

      act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
   }

   [Fact]
   public void AddDatabase_WithSettingsAfterThePlainOverload_LeavesOneSingletonFactory()
   {
      var services = new ServiceCollection();

      services.AddDatabase();
      services.AddDatabase(new DatabaseConnectionFactorySettings { ApplicationName = "settings-after" });

      services.Should().ContainSingle(x => x.ServiceType == typeof(DatabaseConnectionFactory) && x.Lifetime == ServiceLifetime.Singleton);
   }

   [Fact]
   public void AddDatabase_PlainOverloadAfterSettings_LeavesOneSingletonFactory()
   {
      var services = new ServiceCollection();

      services.AddDatabase(new DatabaseConnectionFactorySettings { ApplicationName = "settings-before" });
      services.AddDatabase();

      services.Should().ContainSingle(x => x.ServiceType == typeof(DatabaseConnectionFactory) && x.Lifetime == ServiceLifetime.Singleton);
   }

   [Fact]
   public void AddDatabase_WithSettingsTwice_LeavesOneSingletonFactory()
   {
      var services = new ServiceCollection();

      services.AddDatabase(new DatabaseConnectionFactorySettings { ApplicationName = "first" });
      services.AddDatabase(new DatabaseConnectionFactorySettings { ApplicationName = "last" });

      services.Should().ContainSingle(x => x.ServiceType == typeof(DatabaseConnectionFactory) && x.Lifetime == ServiceLifetime.Singleton);
   }
}
