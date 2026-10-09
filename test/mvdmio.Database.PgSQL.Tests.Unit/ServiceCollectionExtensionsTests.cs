using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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

   [Fact]
   public void AddDatabase_WithNullSettings_Throws()
   {
      var services = new ServiceCollection();

      var act = () => services.AddDatabase(null!);

      act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
   }

   [Fact]
   public void AddDatabase_WithSettingsAfterThePlainOverload_LeavesOneSingletonFactoryBuiltFromTheSettings()
   {
      var services = new ServiceCollection();

      services.AddDatabase();
      services.AddDatabase(new DatabaseConnectionFactorySettings { ApplicationName = "settings-after" });

      ApplicationNameOfTheOnlyFactory(services).Should().Be("settings-after");
   }

   [Fact]
   public void AddDatabase_PlainOverloadAfterSettings_KeepsTheSettingsFactory()
   {
      var services = new ServiceCollection();

      services.AddDatabase(new DatabaseConnectionFactorySettings { ApplicationName = "settings-before" });
      services.AddDatabase();

      ApplicationNameOfTheOnlyFactory(services).Should().Be("settings-before");
   }

   [Fact]
   public void AddDatabase_WithSettingsTwice_TheLastCallWins()
   {
      var services = new ServiceCollection();

      services.AddDatabase(new DatabaseConnectionFactorySettings { ApplicationName = "first" });
      services.AddDatabase(new DatabaseConnectionFactorySettings { ApplicationName = "last" });

      ApplicationNameOfTheOnlyFactory(services).Should().Be("last");
   }

   // Builds the factory the way the container would, then reads the name its data source carries.
   private static string? ApplicationNameOfTheOnlyFactory(ServiceCollection services)
   {
      var descriptor = services.Should().ContainSingle(x => x.ServiceType == typeof(DatabaseConnectionFactory)).Subject;
      descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
      descriptor.ImplementationFactory.Should().NotBeNull();

      using var factory = (DatabaseConnectionFactory)descriptor.ImplementationFactory!(null!);
      var dataSource = factory.BuildDataSource("Host=localhost;Database=test;Username=test;Password=test");

      return new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).ApplicationName;
   }
}
