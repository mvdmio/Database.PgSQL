using AwesomeAssertions;
using mvdmio.Database.PgSQL.Internal;

namespace mvdmio.Database.PgSQL.Tests.Unit.Internal;

public class PoolDefaultsTests
{
   private const string BASE_CONNECTION_STRING = "Host=localhost;Database=test;Username=test;Password=test";
   private const string ENTRY_ASSEMBLY_NAME = "Some.Entry.App";

   [Fact]
   public void Resolve_WithNoKeywords_GivesCapOfTenAndTheEntryAssemblyName()
   {
      var result = PoolDefaults.Resolve(BASE_CONNECTION_STRING, ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(10, ENTRY_ASSEMBLY_NAME));
   }

   [Fact]
   public void Resolve_WithoutEntryAssembly_LeavesTheNameUnset()
   {
      var result = PoolDefaults.Resolve(BASE_CONNECTION_STRING, null);

      result.Should().Be(new PoolSettings(10, null));
   }

   [Theory]
   [InlineData("Maximum Pool Size")]
   [InlineData("maximum pool size")]
   [InlineData("MAXIMUM Pool SIZE")]
   [InlineData("MaxPoolSize")]
   [InlineData("maxpoolsize")]
   [InlineData("MAXPOOLSIZE")]
   public void Resolve_WithACapKeyword_KeywordBeatsTheDefaultCapAndKeepsTheDefaultName(string keyword)
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};{keyword}=42", ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(42, ENTRY_ASSEMBLY_NAME));
   }

   [Theory]
   [InlineData("Application Name")]
   [InlineData("application name")]
   [InlineData("APPLICATION Name")]
   [InlineData("ApplicationName")]
   [InlineData("applicationname")]
   [InlineData("APPLICATIONNAME")]
   public void Resolve_WithANameKeyword_KeywordBeatsTheDefaultNameAndKeepsTheDefaultCap(string keyword)
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};{keyword}=Keyword.App", ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(10, "Keyword.App"));
   }

   [Fact]
   public void Resolve_WithACapKeywordEqualToNpgsqlsDefault_KeepsTheKeywordValue()
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Maximum Pool Size=100", ENTRY_ASSEMBLY_NAME);

      result.MaxPoolSize.Should().Be(100);
   }

   // ADO.NET's connection string parser drops a keyword with an empty value, so Npgsql never sees it either: the keyword
   // counts as absent and the default applies.
   [Fact]
   public void Resolve_WithAnEmptyNameKeyword_TreatsItAsAbsent()
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Application Name=", ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(10, ENTRY_ASSEMBLY_NAME));
   }

   [Fact]
   public void Resolve_WithAnEmptyNameKeywordAndNoEntryAssembly_LeavesTheNameUnset()
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Application Name=", null);

      result.Should().Be(new PoolSettings(10, null));
   }

   [Fact]
   public void Resolve_WithAnEmptyCapKeyword_TreatsItAsAbsent()
   {
      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Maximum Pool Size=", ENTRY_ASSEMBLY_NAME);

      result.Should().Be(new PoolSettings(10, ENTRY_ASSEMBLY_NAME));
   }

   [Fact]
   public void Resolve_WithExplicitCapAndName_SettingsBeatTheKeywords()
   {
      var settings = new DatabaseConnectionFactorySettings { MaxPoolSize = 2, ApplicationName = "Setting.App" };

      var result = PoolDefaults.Resolve(
         $"{BASE_CONNECTION_STRING};Maximum Pool Size=5;Application Name=Keyword.App",
         ENTRY_ASSEMBLY_NAME,
         settings
      );

      result.Should().Be(new PoolSettings(2, "Setting.App"));
   }

   [Fact]
   public void Resolve_WithExplicitCapAndName_SettingsBeatTheDefaults()
   {
      var settings = new DatabaseConnectionFactorySettings { MaxPoolSize = 2, ApplicationName = "Setting.App" };

      var result = PoolDefaults.Resolve(BASE_CONNECTION_STRING, ENTRY_ASSEMBLY_NAME, settings);

      result.Should().Be(new PoolSettings(2, "Setting.App"));
   }

   [Fact]
   public void Resolve_WithOnlyAnExplicitName_KeepsTheKeywordCap()
   {
      var settings = new DatabaseConnectionFactorySettings { ApplicationName = "Setting.App" };

      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Maximum Pool Size=5", ENTRY_ASSEMBLY_NAME, settings);

      result.Should().Be(new PoolSettings(5, "Setting.App"));
   }

   [Fact]
   public void Resolve_WithOnlyAnExplicitName_KeepsTheDefaultCap()
   {
      var settings = new DatabaseConnectionFactorySettings { ApplicationName = "Setting.App" };

      var result = PoolDefaults.Resolve(BASE_CONNECTION_STRING, ENTRY_ASSEMBLY_NAME, settings);

      result.Should().Be(new PoolSettings(10, "Setting.App"));
   }

   [Fact]
   public void Resolve_WithOnlyAnExplicitCap_KeepsTheKeywordName()
   {
      var settings = new DatabaseConnectionFactorySettings { MaxPoolSize = 2 };

      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Application Name=Keyword.App", ENTRY_ASSEMBLY_NAME, settings);

      result.Should().Be(new PoolSettings(2, "Keyword.App"));
   }

   [Fact]
   public void Resolve_WithOnlyAnExplicitCap_KeepsTheEntryAssemblyName()
   {
      var settings = new DatabaseConnectionFactorySettings { MaxPoolSize = 2 };

      var result = PoolDefaults.Resolve(BASE_CONNECTION_STRING, ENTRY_ASSEMBLY_NAME, settings);

      result.Should().Be(new PoolSettings(2, ENTRY_ASSEMBLY_NAME));
   }

   [Fact]
   public void Resolve_WithEmptySettings_BehavesAsWithoutSettings()
   {
      var result = PoolDefaults.Resolve(BASE_CONNECTION_STRING, ENTRY_ASSEMBLY_NAME, new DatabaseConnectionFactorySettings());

      result.Should().Be(new PoolSettings(10, ENTRY_ASSEMBLY_NAME));
   }

   [Fact]
   public void Resolve_WithAnExplicitNameAndNoEntryAssembly_UsesTheExplicitName()
   {
      var settings = new DatabaseConnectionFactorySettings { ApplicationName = "Setting.App" };

      var result = PoolDefaults.Resolve(BASE_CONNECTION_STRING, null, settings);

      result.Should().Be(new PoolSettings(10, "Setting.App"));
   }

   // A value that is set is used exactly as given, so an explicit empty name beats the keyword and the default.
   [Fact]
   public void Resolve_WithAnExplicitEmptyName_KeepsItAsGiven()
   {
      var settings = new DatabaseConnectionFactorySettings { ApplicationName = "" };

      var result = PoolDefaults.Resolve($"{BASE_CONNECTION_STRING};Application Name=Keyword.App", ENTRY_ASSEMBLY_NAME, settings);

      result.Should().Be(new PoolSettings(10, ""));
   }
}
