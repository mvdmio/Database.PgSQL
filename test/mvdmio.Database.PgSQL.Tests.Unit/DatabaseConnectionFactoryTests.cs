using AwesomeAssertions;
using Npgsql;

namespace mvdmio.Database.PgSQL.Tests.Unit;

public class DatabaseConnectionFactoryTests
{
   private const string CONNECTION_STRING = "Host=localhost;Database=test;Username=test;Password=test";

   [Fact]
   public async Task BuildDataSource_SameConnectionStringUnderContention_BuildsOnceAndReturnsSameInstance()
   {
      const int CONCURRENCY = 64;

      using var factory = new DatabaseConnectionFactory();

      var builderInvocations = 0;
      void BuilderAction(NpgsqlDataSourceBuilder _) => Interlocked.Increment(ref builderInvocations);

      var startSignal = new TaskCompletionSource();

      var tasks = Enumerable.Range(0, CONCURRENCY).Select(_ => Task.Run(async () =>
      {
         await startSignal.Task;
         return factory.BuildDataSource(CONNECTION_STRING, BuilderAction);
      })).ToList();

      startSignal.SetResult();

      var dataSources = await Task.WhenAll(tasks);

      builderInvocations.Should().Be(1);

      dataSources.Should().OnlyContain(x => ReferenceEquals(x, dataSources[0]));
   }

   [Fact]
   public void BuildDataSource_DistinctConnectionStrings_ReturnsDistinctInstances()
   {
      using var factory = new DatabaseConnectionFactory();

      var first = factory.BuildDataSource("Host=localhost;Database=one;Username=test;Password=test");
      var second = factory.BuildDataSource("Host=localhost;Database=two;Username=test;Password=test");

      first.Should().NotBeSameAs(second);
   }

   [Fact]
   public void BuildDataSource_SameConnectionString_ReturnsCachedInstance()
   {
      using var factory = new DatabaseConnectionFactory();

      var first = factory.BuildDataSource(CONNECTION_STRING);
      var second = factory.BuildDataSource(CONNECTION_STRING);

      first.Should().BeSameAs(second);
   }

   [Fact]
   public void BuildDataSource_AfterDispose_ThrowsObjectDisposedException()
   {
      var factory = new DatabaseConnectionFactory();
      factory.Dispose();

      var act = () => factory.BuildDataSource(CONNECTION_STRING);

      act.Should().Throw<ObjectDisposedException>();
   }

   [Fact]
   public void BuildDataSource_WithoutKeywords_CapsThePoolAtTen()
   {
      using var factory = new DatabaseConnectionFactory();

      var dataSource = factory.BuildDataSource(CONNECTION_STRING);

      new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).MaxPoolSize.Should().Be(10);
   }

   [Fact]
   public void BuildDataSource_WithABuilderActionSettingCapAndName_TheActionOverridesTheDefaults()
   {
      using var factory = new DatabaseConnectionFactory();

      var dataSource = factory.BuildDataSource(
         CONNECTION_STRING,
         builder =>
         {
            builder.ConnectionStringBuilder.MaxPoolSize = 37;
            builder.ConnectionStringBuilder.ApplicationName = "Action.App";
         }
      );

      AssertReportedCapAndName(dataSource, 37, "Action.App");
   }

   [Fact]
   public void Constructor_WithNullSettings_ThrowsArgumentNullException()
   {
      var act = () => new DatabaseConnectionFactory(null!);

      act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
   }

   [Fact]
   public void BuildDataSource_WithSettings_AppliesThemOverTheKeywords()
   {
      using var factory = new DatabaseConnectionFactory(new DatabaseConnectionFactorySettings { MaxPoolSize = 2, ApplicationName = "Setting.App" });

      var dataSource = factory.BuildDataSource($"{CONNECTION_STRING};Maximum Pool Size=5;Application Name=Keyword.App");

      AssertReportedCapAndName(dataSource, 2, "Setting.App");
   }

   [Fact]
   public void BuildDataSource_WithSettingsAndABuilderActionThatSetsNothing_StillAppliesTheSettings()
   {
      using var factory = new DatabaseConnectionFactory(new DatabaseConnectionFactorySettings { MaxPoolSize = 2, ApplicationName = "Setting.App" });

      var dataSource = factory.BuildDataSource(CONNECTION_STRING, _ => { });

      AssertReportedCapAndName(dataSource, 2, "Setting.App");
   }

   [Fact]
   public void BuildConnection_WithSettings_AppliesThemToTheDataSource()
   {
      using var factory = new DatabaseConnectionFactory(new DatabaseConnectionFactorySettings { MaxPoolSize = 2, ApplicationName = "Setting.App" });

      using var db = factory.BuildConnection(CONNECTION_STRING);

      AssertReportedCapAndName(factory.BuildDataSource(CONNECTION_STRING), 2, "Setting.App");
   }

   [Fact]
   public void BuildConnection_WithSettingsAndABuilderActionThatSetsNothing_StillAppliesTheSettings()
   {
      using var factory = new DatabaseConnectionFactory(new DatabaseConnectionFactorySettings { MaxPoolSize = 2, ApplicationName = "Setting.App" });

      using var db = factory.BuildConnection(CONNECTION_STRING, _ => { });

      AssertReportedCapAndName(factory.BuildDataSource(CONNECTION_STRING), 2, "Setting.App");
   }

   [Fact]
   public void BuildDataSource_WithAnExplicitEmptyName_BuildsWithTheNameCleared()
   {
      using var factory = new DatabaseConnectionFactory(new DatabaseConnectionFactorySettings { ApplicationName = "" });

      var dataSource = factory.BuildDataSource($"{CONNECTION_STRING};Application Name=Keyword.App");

      new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).ApplicationName.Should().BeNullOrEmpty();
   }

   [Fact]
   public void BuildDataSource_WithSettingsAndABuilderActionSettingCapAndName_TheActionOverridesTheSettings()
   {
      using var factory = new DatabaseConnectionFactory(new DatabaseConnectionFactorySettings { MaxPoolSize = 2, ApplicationName = "Setting.App" });

      var dataSource = factory.BuildDataSource(
         CONNECTION_STRING,
         builder =>
         {
            builder.ConnectionStringBuilder.MaxPoolSize = 37;
            builder.ConnectionStringBuilder.ApplicationName = "Action.App";
         }
      );

      AssertReportedCapAndName(dataSource, 37, "Action.App");
   }

   private static void AssertReportedCapAndName(NpgsqlDataSource dataSource, int cap, string name)
   {
      var reported = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);
      reported.MaxPoolSize.Should().Be(cap);
      reported.ApplicationName.Should().Be(name);
   }
}
